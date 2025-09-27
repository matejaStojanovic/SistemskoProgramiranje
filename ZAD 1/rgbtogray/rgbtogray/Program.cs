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
            string currentDirectory = Directory.GetCurrentDirectory();
            Console.WriteLine($"Trenutni direktorijum: {currentDirectory}");

            listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:5050/");

            listener.Start();
            Console.WriteLine("Server pokrenut na http://localhost:5050/");

            while (true)
            {
                var context = await listener.GetContextAsync();
                ThreadPool.QueueUserWorkItem(HandleRequest, context);
            }
        }

        private static void HandleRequest(object obj)
        {
            var context = (HttpListenerContext)obj;
            var request = context.Request;
            var response = context.Response;
            string imageName = request.Url.AbsolutePath.TrimStart('/');

            Console.WriteLine($"Primljen zahtev za: {imageName}");

            byte[] cachedResponse;
            if (cache.TryGetValue(imageName, out cachedResponse))
            {
                Console.WriteLine($"Slanje kesiranog odgovora za: {imageName}");
                response.ContentType = "image/jpeg";
                response.ContentLength64 = cachedResponse.Length;
                response.OutputStream.Write(cachedResponse, 0, cachedResponse.Length);
                response.Close();
                return;
            }
            try
            {
                string imagePath = Path.Combine("wwwroot", "images", imageName);
                Console.WriteLine($"Trazena slika: {imagePath}");
                if (!File.Exists(imagePath))
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    byte[] notFoundMessage = System.Text.Encoding.UTF8.GetBytes("Image not found.");
                    response.OutputStream.Write(notFoundMessage, 0, notFoundMessage.Length);
                    Console.WriteLine($"Greska: Slika nije pronadjena - {imageName}");
                    response.Close();
                    return;
                }
                using (Bitmap originalImage = new Bitmap(imagePath))
                {
                    Bitmap grayscaleImage = ConvertToGrayscale(originalImage);

                    using (MemoryStream ms = new MemoryStream())
                    {
                        grayscaleImage.Save(ms, ImageFormat.Jpeg);
                        byte[] imageBytes = ms.ToArray();

                        lock (cacheLock)
                        {
                            cache[imageName] = imageBytes;
                        }

                        response.ContentType = "image/jpeg";
                        response.ContentLength64 = imageBytes.Length;
                        response.OutputStream.Write(imageBytes, 0, imageBytes.Length);
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
                response.OutputStream.Write(errorMessage, 0, errorMessage.Length);
                response.Close();
            }
        }

        private static Bitmap ConvertToGrayscale(Bitmap original)
        {
            Bitmap grayscaleBitmap = new Bitmap(original.Width, original.Height);
            Console.WriteLine("Konvertovanje slike u grayscale...");
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