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

namespace Bank
{
    public class Account
    {
        public string UserID { get; set; }
        public string Name { get; set; }
        public double Balance { get; set; }
    }

    public class BankService : StatefulService
    {
        private Dictionary<string, Account> _currentAccounts = new Dictionary<string, Account>();
        private Dictionary<string, Account> _previousAccounts = new Dictionary<string, Account>();

        public BankService(StatefulServiceContext context) : base(context)
        {
            // Postavi početne vrednosti
            _currentAccounts["U1"] = new Account { UserID = "U1", Name = "Petar Petrović", Balance = 5000 };
            _currentAccounts["U2"] = new Account { UserID = "U2", Name = "Marko Marković", Balance = 3000 };
            _currentAccounts["U3"] = new Account { UserID = "U3", Name = "Ana Anić", Balance = 10000 };
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
                return JsonSerializer.Serialize(_currentAccounts.Values);
            }
            else if (path == "/enlist")
            {
                var req = JsonSerializer.Deserialize<Dictionary<string, object>>(body);
                string userID = req["userID"].ToString();
                double amount = double.Parse(req["amount"].ToString());

                if (_currentAccounts.ContainsKey(userID) && _currentAccounts[userID].Balance >= amount)
                {
                    // KORAK 1: Sačuvaj KOMPLETNO trenutno stanje u _previous (pre bilo kakve izmene)
                    _previousAccounts.Clear();
                    foreach (var kvp in _currentAccounts)
                    {
                        _previousAccounts[kvp.Key] = new Account
                        {
                            UserID = kvp.Value.UserID,
                            Name = kvp.Value.Name,
                            Balance = kvp.Value.Balance
                        };
                    }

                    // KORAK 2: Primeni promenu na _current
                    _currentAccounts[userID].Balance -= amount;
                    return JsonSerializer.Serialize(new { success = true });
                }
                return JsonSerializer.Serialize(new { success = false, error = "Nedovoljno sredstava" });
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
                if (_previousAccounts.Count > 0)
                {
                    _currentAccounts.Clear();
                    foreach (var kvp in _previousAccounts)
                    {
                        _currentAccounts[kvp.Key] = new Account
                        {
                            UserID = kvp.Value.UserID,
                            Name = kvp.Value.Name,
                            Balance = kvp.Value.Balance
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