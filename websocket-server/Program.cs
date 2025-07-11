using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Drawing;
using System.Drawing.Printing;

namespace WebSocketPNGPrinter
{
    public class WebSocketMessage
    {
        public string Action { get; set; }
        public string ImageData { get; set; }
        public string FileName { get; set; }
    }

    class Program
    {
        private static HttpListener listener;
        private static CancellationTokenSource cts;

        static void Main(string[] args)
        {
            listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:8080/");
            cts = new CancellationTokenSource();

            Console.WriteLine("PNG Yazdırma Uygulaması başlatıldı. Kapatmak için CTRL+C tuşlarına basın.");

            try
            {
                listener.Start();
                Console.WriteLine("WebSocket sunucusu 8080 portunda dinliyor.");

                while (!cts.IsCancellationRequested)
                {
                    HttpListenerContext context = listener.GetContext();
                    if (context.Request.IsWebSocketRequest)
                    {
                        ProcessWebSocketRequest(context);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Hata: {ex.Message}");
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async void ProcessWebSocketRequest(HttpListenerContext context)
        {
            HttpListenerWebSocketContext webSocketContext = await context.AcceptWebSocketAsync(null);
            WebSocket webSocket = webSocketContext.WebSocket;

            Console.WriteLine("İstemci bağlandı");

            StringBuilder messageBuilder = new StringBuilder();
            byte[] buffer = new byte[1024 * 4];
            while (webSocket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    messageBuilder.Append(message);

                    if (result.EndOfMessage)
                    {
                        string fullMessage = messageBuilder.ToString();
                        await ProcessMessage(webSocket, fullMessage);
                        messageBuilder.Clear();
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cts.Token);
                }
            }

            Console.WriteLine("İstemci bağlantısı kesildi");
        }

        private static async Task ProcessMessage(WebSocket webSocket, string message)
        {
            try
            {
                JObject jsonObject = JObject.Parse(message);
                string action = jsonObject["action"]?.ToString();
                string imageData = jsonObject["imageData"]?.ToString();
                string fileName = jsonObject["fileName"]?.ToString();

                if (action == "print" && !string.IsNullOrEmpty(imageData) && !string.IsNullOrEmpty(fileName))
                {
                    byte[] imageBuffer = Convert.FromBase64String(imageData);
                    using (Image image = Image.FromStream(new MemoryStream(imageBuffer)))
                    {
                        PrintPNG(image, fileName);
                    }
                    await SendWebSocketMessage(webSocket, new WebSocketMessage { Action = "printSuccess", ImageData = "PNG yazdırma başarılı" });
                }
                else
                {
                    Console.WriteLine($"Geçersiz veya işlenemeyen mesaj: {message.Substring(0, Math.Min(100, message.Length))}...");
                }
            }
            catch (JsonReaderException jsonEx)
            {
                Console.WriteLine($"JSON ayrıştırma hatası: {jsonEx.Message}");
                Console.WriteLine($"Hata konumu: Satır {jsonEx.LineNumber}, Pozisyon {jsonEx.LinePosition}");
                Console.WriteLine($"Mesajın ilk 100 karakteri: {message.Substring(0, Math.Min(100, message.Length))}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Mesaj işleme hatası: {ex.Message}");
                await SendWebSocketMessage(webSocket, new WebSocketMessage { Action = "error", ImageData = ex.Message });
            }
        }

        private static void PrintPNG(Image image, string fileName)
        {
            try
            {
                using (var printDocument = new PrintDocument())
                {
                    printDocument.PrinterSettings.PrinterName = new PrinterSettings().PrinterName;
                    printDocument.PrintPage += (sender, e) =>
                    {
                        e.Graphics.DrawImage(image, e.PageBounds);
                    };
                    printDocument.Print();
                }

                Console.WriteLine($"{fileName} yazdırma işlemi tamamlandı.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PNG yazdırma hatası: {ex.Message}");
            }
        }

        private static async Task SendWebSocketMessage(WebSocket webSocket, WebSocketMessage message)
        {
            string json = JsonConvert.SerializeObject(message);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
        }
    }
}