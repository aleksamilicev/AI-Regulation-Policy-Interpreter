using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Coordinator
{
    public class BookOrder
    {
        public string Title { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }

    public class Coordinator : StatefulService
    {
        private readonly HttpClient _httpClient = new HttpClient();

        public Coordinator(StatefulServiceContext context) : base(context) { }

        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new[] {
                new ServiceReplicaListener(context =>
                    new HttpCommunicationListener(context, HandleRequest))
            };
        }

        private async Task<string> HandleRequest(string path, string body)
        {
            if (path == "/process")
            {
                var req = JsonSerializer.Deserialize<Dictionary<string, object>>(body);
                var order = JsonSerializer.Deserialize<BookOrder>(req["order"].ToString());
                string userID = req["userID"].ToString();

                return await ProcessOrder(order, userID);
            }
            return JsonSerializer.Serialize(new { error = "Unknown endpoint" });
        }

        private async Task<string> ProcessOrder(BookOrder order, string userID)
        {
            string bookstoreUrl = "http://localhost:8081"; // Bookstore port
            string bankUrl = "http://localhost:8082"; // Bank port

            try
            {
                // 1. Proveri cenu knjige
                var priceReq = new { bookID = order.Title };
                var priceRes = await _httpClient.PostAsync($"{bookstoreUrl}/price",
                    new StringContent(JsonSerializer.Serialize(priceReq), Encoding.UTF8, "application/json"));
                double price = JsonSerializer.Deserialize<double>(await priceRes.Content.ReadAsStringAsync());

                if (price == 0)
                    return JsonSerializer.Serialize(new { success = false, message = "Knjiga ne postoji" });

                double totalAmount = price * order.Quantity;

                // 2. Rezerviši sredstva u banci
                var bankReq = new { userID, amount = totalAmount };
                var bankRes = await _httpClient.PostAsync($"{bankUrl}/enlist",
                    new StringContent(JsonSerializer.Serialize(bankReq), Encoding.UTF8, "application/json"));
                var bankResult = JsonSerializer.Deserialize<Dictionary<string, object>>(await bankRes.Content.ReadAsStringAsync());

                if (!bool.Parse(bankResult["success"].ToString()))
                    return JsonSerializer.Serialize(new { success = false, message = "Nedovoljno sredstava" });

                // 3. Rezerviši knjige
                var bookReq = new { bookID = order.Title, count = order.Quantity };
                var bookRes = await _httpClient.PostAsync($"{bookstoreUrl}/enlist",
                    new StringContent(JsonSerializer.Serialize(bookReq), Encoding.UTF8, "application/json"));
                var bookResult = JsonSerializer.Deserialize<Dictionary<string, object>>(await bookRes.Content.ReadAsStringAsync());

                if (!bool.Parse(bookResult["success"].ToString()))
                {
                    await _httpClient.PostAsync($"{bankUrl}/rollback", null);
                    return JsonSerializer.Serialize(new { success = false, message = "Nedovoljno knjiga" });
                }

                // 4. Prepare faza
                var bankPrepare = await _httpClient.PostAsync($"{bankUrl}/prepare", null);
                var bookPrepare = await _httpClient.PostAsync($"{bookstoreUrl}/prepare", null);

                // 5. Commit
                await _httpClient.PostAsync($"{bankUrl}/commit", null);
                await _httpClient.PostAsync($"{bookstoreUrl}/commit", null);

                return JsonSerializer.Serialize(new { success = true, message = $"Uspešna kupovina! Plaæeno: {totalAmount} RSD" });
            }
            catch (Exception ex)
            {
                // Rollback
                try
                {
                    await _httpClient.PostAsync($"{bankUrl}/rollback", null);
                    await _httpClient.PostAsync($"{bookstoreUrl}/rollback", null);
                }
                catch { }

                return JsonSerializer.Serialize(new { success = false, message = $"Greška: {ex.Message}" });
            }
        }
    }

    public class HttpCommunicationListener : ICommunicationListener
    {
        private readonly StatefulServiceContext _context;
        private readonly Func<string, string, Task<string>> _handleRequest;
        private HttpListener _listener;

        public HttpCommunicationListener(StatefulServiceContext context, Func<string, string, Task<string>> handleRequest)
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

                var response = await _handleRequest(ctx.Request.Url.AbsolutePath, body);
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