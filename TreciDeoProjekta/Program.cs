using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Reactive.Linq;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace TreciDeoProjekta
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:5050/");
            listener.Start();
            Console.WriteLine("Server started on http://localhost:5050/");

            var requestStream = Observable.Create<HttpListenerContext>(async (observer, ct) =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var context = await listener.GetContextAsync();
                        observer.OnNext(context);
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            });

            requestStream
                .ObserveOn(System.Reactive.Concurrency.ThreadPoolScheduler.Instance)
                .Subscribe(
                    async context =>
                    {
                        await HandleRequestAsync(context);
                    },
                    ex => Console.WriteLine($"Greška u streamu: {ex.Message}")
                );

            Console.WriteLine("Pritisnite Enter za prekid rada servera...");
            Console.ReadLine();
        }

        static async Task HandleRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            string city = request.QueryString["city"];
            string date = request.QueryString["date"];

            string outputLabel = (!string.IsNullOrEmpty(city) && !string.IsNullOrEmpty(date))
                ? $"Informacije o vremenu za {city} na datum {date}"
                : "Nije izabrano mesto i vreme";

            double minHumidity = 0, maxHumidity = 0, avgHumidity = 0;
            double minUV = 0, maxUV = 0, avgUV = 0;
            double minVis = 0, maxVis = 0, avgVis = 0;

            if (!string.IsNullOrEmpty(city) && !string.IsNullOrEmpty(date))
            {
                try
                {
                    var weatherJson = await GetWeatherAsync(city, date);
                    JObject obj = JObject.Parse(weatherJson);

                    var humidityArray = obj["hourly"]?["relative_humidity_2m"]?.ToObject<double[]>();
                    if (humidityArray != null && humidityArray.Length > 0)
                    {
                        var observable = humidityArray.ToObservable();
                        minHumidity = await observable.Min();
                        maxHumidity = await observable.Max();
                        avgHumidity = await observable.Average();
                    }

                    var uvArray = obj["hourly"]?["uv_index"]?.ToObject<double[]>();
                    if (uvArray != null && uvArray.Length > 0)
                    {
                        var observable = uvArray.ToObservable();
                        minUV = await observable.Min();
                        maxUV = await observable.Max();
                        avgUV = await observable.Average();
                    }

                    var visibilityArray = obj["hourly"]?["visibility"]?.ToObject<double[]>();
                    if (visibilityArray != null && visibilityArray.Length > 0)
                    {
                        var observable = visibilityArray.ToObservable();
                        minVis = await observable.Min();
                        maxVis = await observable.Max();
                        avgVis = await observable.Average();
                    }
                    Console.WriteLine("Uspesno smo dobili podatke i prosledili klijentu koji ih je zatrazio..");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Greska prilikom pribavljanja podataka: {ex.Message}");
                    outputLabel += " (nije moguce dobiti podatke)";
                }
            }

            string html = $@"
            <html>
            <head>
            <title>Vremenska Prognoza</title>
            <style>
            body {{
                margin: 0;
                font-family: Arial, sans-serif;
                background: linear-gradient(167deg, #89f7fe, #6666ff);
                display: flex;
                justify-content: center;
                align-items: center;
                min-height: 100vh;
            }}
            .container {{
                background-color: rgba(255, 255, 255, 0.9);
                padding: 30px;
                border-radius: 15px;
                box-shadow: 0 8px 20px rgba(0,0,0,0.3);
                text-align: center;
                width: 350px;
            }}
            h1 {{ margin-bottom: 20px; color: #333; }}
            form label {{ display: block; margin-top: 10px; font-weight: bold; color: #333; }}
            form input[type='text'], form input[type='date'] {{
                 width: 100%; padding: 8px; margin-top: 5px;
                 border-radius: 5px; border: 1px solid #ccc; box-sizing: border-box;
            }}
            form input[type='submit'] {{
                margin-top: 15px; padding: 10px 20px; border: none;
                border-radius: 8px; background-color: #66a6ff;
                color: white; font-weight: bold; cursor: pointer; transition: background-color 0.3s; 
            }}
            form input[type='submit']:hover {{ background-color: #89f7fe; }}
            #output {{ margin-top: 25px; text-align: left; color: #333; }}
            #output h2 {{ text-align: center; color: #444; }}
            </style>
            </head>
            <body>
                <div class='container'>
                <h1>VREMENSKA PROGNOZA</h1>
                <form method='get' action='/'>
                <label>Unesite mesto:</label>
                <input type='text' name='city' value='{city ?? ""}'/>
                <label>Unesite datum:</label>
                <input type='date' name='date' value='{date ?? ""}'/>
                <input type='submit' value='Prikazi'/>
             </form>
            <div id='output'>
                <h2>{outputLabel}</h2>
                <p>Minimalna vlaznost: {minHumidity}</p>
                <p>Maximalna vlaznost: {maxHumidity}</p>
                <p>Prosecna vlaznost: {avgHumidity:F2}</p>
                <p>UV indeks: avg {avgUV:F2} (min {minUV}, max {maxUV})</p>
                <p>Vidljivost: avg {avgVis:F2} m (min {minVis}, max {maxVis})</p>
            </div>
            </div>
            <script src=""https://unpkg.com/rxjs@7/dist/bundles/rxjs.umd.min.js""></script>
            </body>
            </html>";

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.ContentLength64 = buffer.Length;
            response.ContentType = "text/html";
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();

            if (!(string.IsNullOrEmpty(city) || string.IsNullOrEmpty(date)))
            {
                Console.WriteLine($"Request processed for city='{city}' date='{date}'");
            }
        }

        static async Task<(double lat, double lon)> GetCoordinatesAsync(string city)
        {
            using (var client = new HttpClient())
            {
                string geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}";
                string geoJson = await client.GetStringAsync(geoUrl);
                JObject geoObj = JObject.Parse(geoJson);

                var firstResult = geoObj["results"]?.First;
                if (firstResult != null)
                {
                    double lat = firstResult["latitude"].Value<double>();
                    double lon = firstResult["longitude"].Value<double>();
                    return (lat, lon);
                }
                else
                {
                    throw new Exception("Grad nije pronađen");
                }
            }
        }

        static async Task<string> GetWeatherAsync(string city, string date)
        {
            var (lat, lon) = await GetCoordinatesAsync(city);
            using (var client = new HttpClient())
            {
                string url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
                             $"&hourly=relative_humidity_2m,uv_index,visibility&start_date={date}&end_date={date}";
                return await client.GetStringAsync(url);
            }
        }
    }
}
