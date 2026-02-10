using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bookstore
{
    public class Book
    {
        public string ID { get; set; }
        public string Title { get; set; }
        public double Price { get; set; }
        public int Stock { get; set; }
    }

    public class Bookstore : StatefulService
    {
        private Dictionary<string, Book> _currentBooks = new Dictionary<string, Book>();
        private Dictionary<string, Book> _previousBooks = new Dictionary<string, Book>();

        public Bookstore(StatefulServiceContext context) : base(context)
        {
            // Postavi početne vrednosti
            _currentBooks["B1"] = new Book { ID = "B1", Title = "Knjiga 1", Price = 1500, Stock = 10 };
            _currentBooks["B2"] = new Book { ID = "B2", Title = "Knjiga 2", Price = 2000, Stock = 5 };
            _currentBooks["B3"] = new Book { ID = "B3", Title = "Knjiga 3", Price = 1200, Stock = 8 };
        }

        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new[] {
                new ServiceReplicaListener(context =>
                    new HttpCommunicationListener(context, HandleRequest))
            };
        }

        private string HandleRequest(string path, string body)
        {
            if (path == "/list")
            {
                return JsonSerializer.Serialize(_currentBooks.Values);
            }
            else if (path == "/price")
            {
                var req = JsonSerializer.Deserialize<Dictionary<string, string>>(body);
                string bookID = req["bookID"];
                return JsonSerializer.Serialize(_currentBooks.ContainsKey(bookID) ? _currentBooks[bookID].Price : 0);
            }
            else if (path == "/enlist")
            {
                var req = JsonSerializer.Deserialize<Dictionary<string, object>>(body);
                string bookID = req["bookID"].ToString();
                int count = int.Parse(req["count"].ToString());

                if (_currentBooks.ContainsKey(bookID) && _currentBooks[bookID].Stock >= count)
                {
                    // KORAK 1: Sačuvaj KOMPLETNO trenutno stanje u _previous (pre bilo kakve izmene)
                    _previousBooks.Clear();
                    foreach (var kvp in _currentBooks)
                    {
                        _previousBooks[kvp.Key] = new Book
                        {
                            ID = kvp.Value.ID,
                            Title = kvp.Value.Title,
                            Price = kvp.Value.Price,
                            Stock = kvp.Value.Stock
                        };
                    }

                    // KORAK 2: Primeni promenu na _current
                    _currentBooks[bookID].Stock -= count;
                    return JsonSerializer.Serialize(new { success = true });
                }
                return JsonSerializer.Serialize(new { success = false, error = "Nedovoljno knjiga" });
            }
            else if (path == "/prepare")
            {
                return JsonSerializer.Serialize(new { ready = true });
            }
            else if (path == "/commit")
            {
                // Transakcija je uspešna - _previous više nije potreban, ali ga ne brišemo
                // (ostavimo ga kao backup za sledeću transakciju)
                return JsonSerializer.Serialize(new { success = true });
            }
            else if (path == "/rollback")
            {
                // Vrati _current na stanje iz _previous
                if (_previousBooks.Count > 0)
                {
                    _currentBooks.Clear();
                    foreach (var kvp in _previousBooks)
                    {
                        _currentBooks[kvp.Key] = new Book
                        {
                            ID = kvp.Value.ID,
                            Title = kvp.Value.Title,
                            Price = kvp.Value.Price,
                            Stock = kvp.Value.Stock
                        };
                    }
                }
                return JsonSerializer.Serialize(new { success = true });
            }

            return JsonSerializer.Serialize(new { error = "Unknown endpoint" });
        }
    }

    public class HttpCommunicationListener : ICommunicationListener
    {
        private readonly StatefulServiceContext _context;
        private readonly Func<string, string, string> _handleRequest;
        private HttpListener _listener;

        public HttpCommunicationListener(StatefulServiceContext context, Func<string, string, string> handleRequest)
        {
            _context = context;
            _handleRequest = handleRequest;
        }

        public Task<string> OpenAsync(CancellationToken cancellationToken)
        {
            var endpoint = _context.CodePackageActivationContext.GetEndpoint("ServiceEndpoint");
            string url = $"http://+:{endpoint.Port}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(url);
            _listener.Start();
            Task.Run(() => ProcessRequests(cancellationToken));
            return Task.FromResult(url.Replace("+", "localhost"));
        }

        private async Task ProcessRequests(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var ctx = await _listener.GetContextAsync();
                string body = "";
                using (var reader = new StreamReader(ctx.Request.InputStream))
                    body = await reader.ReadToEndAsync();

                var response = _handleRequest(ctx.Request.Url.AbsolutePath, body);
                var buffer = Encoding.UTF8.GetBytes(response);
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                ctx.Response.Close();
            }
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            _listener?.Stop();
            return Task.CompletedTask;
        }

        public void Abort() => _listener?.Abort();
    }
}