using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;

namespace rgbtogray
{
    internal class Program
    {
        private static readonly ConcurrentDictionary<string, byte[]> cache = new ConcurrentDictionary<string, byte[]>();
        private static HttpListener listener;
        private static readonly object cacheLock = new object();

        static async Task Main(string[] args)
        {
            listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:5050/");

            listener.Start();
            Console.WriteLine("Server pokrenut na http://localhost:5050/");

            while (true)
            {
                var context = await listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context));
            }
        }

        private static async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            string imageName = request.Url.AbsolutePath.TrimStart('/');

            Console.WriteLine($"Primi zahtev za: {imageName}");

            byte[] cachedResponse;
            if (cache.TryGetValue(imageName, out cachedResponse))
            {
                Console.WriteLine($"Slanje kesiranog odgovora za: {imageName}");
                response.ContentType = "image/jpeg";
                response.ContentLength64 = cachedResponse.Length;
                await response.OutputStream.WriteAsync(cachedResponse, 0, cachedResponse.Length);
                response.Close();
                return;
            }

            try
            {
                string currentDirectory = Directory.GetCurrentDirectory();
                Console.WriteLine($"Trenutni direktorijum: {currentDirectory}");

                string imagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", imageName);
                Console.WriteLine($"Trazena slika: {imagePath}");

                if (!File.Exists(imagePath))
                {
                    Console.WriteLine($"Greska: Slika nije pronađena - {imagePath}");
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    byte[] notFoundMessage = System.Text.Encoding.UTF8.GetBytes("Image not found.");
                    await response.OutputStream.WriteAsync(notFoundMessage, 0, notFoundMessage.Length);
                    response.Close();
                    return;
                }

                using (var originalImage = await Task.Run(() => new Bitmap(imagePath)))
                {
                    Console.WriteLine("Konvertovanje slike u grayscale...");
                    var grayscaleImage = ConvertToGrayscale(originalImage);
                    using (MemoryStream ms = new MemoryStream())
                    {
                        await Task.Run(() => grayscaleImage.Save(ms, ImageFormat.Jpeg));
                        byte[] imageBytes = ms.ToArray();

                        lock (cacheLock)
                        {
                            cache[imageName] = imageBytes;
                        }

                        response.ContentType = "image/jpeg";
                        response.ContentLength64 = imageBytes.Length;
                        await response.OutputStream.WriteAsync(imageBytes, 0, imageBytes.Length);
                        response.Close();

                        Console.WriteLine($"Obradjena slika: {imageName}, uspesno poslata klijentu.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Greska prilikom obrade zahteva: {ex.Message}");
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                byte[] errorMessage = System.Text.Encoding.UTF8.GetBytes("Internal server error.");
                await response.OutputStream.WriteAsync(errorMessage, 0, errorMessage.Length);
                response.Close();
            }
        }

        private static Bitmap ConvertToGrayscale(Bitmap original)
        {
            Bitmap grayscaleBitmap = new Bitmap(original.Width, original.Height);

            for (int y = 0; y < original.Height; y++)
            {
                for (int x = 0; x < original.Width; x++)
                {
                    Color pixelColor = original.GetPixel(x, y);
                    int grayValue = (int)(pixelColor.R * 0.3 + pixelColor.G * 0.59 + pixelColor.B * 0.11);
                    Color grayColor = Color.FromArgb(grayValue, grayValue, grayValue);
                    grayscaleBitmap.SetPixel(x, y, grayColor);
                }
            }
            return grayscaleBitmap;
        }
    }
}
