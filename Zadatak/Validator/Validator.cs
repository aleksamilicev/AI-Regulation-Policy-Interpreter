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

namespace Validator
{
    public class Validator : StatelessService
    {
        public Validator(StatelessServiceContext context) : base(context) { }

        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new[] {
            new ServiceInstanceListener(context => new HttpCommunicationListener(context, ProcessRequest))
        };
        }

        private ValidationResult ProcessRequest(BookOrder order)
        {
            if (string.IsNullOrWhiteSpace(order?.Title))
                return new ValidationResult { IsValid = false, Message = "Naziv knjige je obavezan" };

            if (order.Price <= 0)
                return new ValidationResult { IsValid = false, Message = "Cena mora biti veca od 0" };

            if (order.Quantity <= 0)
                return new ValidationResult { IsValid = false, Message = "Kolicina mora biti veca od 0" };

            return new ValidationResult { IsValid = true, Message = "Validacija uspesna" };
        }
    }

    public class HttpCommunicationListener : ICommunicationListener
    {
        private readonly StatelessServiceContext _context;
        private readonly Func<BookOrder, ValidationResult> _processRequest;
        private HttpListener _listener;

        public HttpCommunicationListener(StatelessServiceContext context, Func<BookOrder, ValidationResult> processRequest)
        {
            _context = context;
            _processRequest = processRequest;
        }

        public Task<string> OpenAsync(CancellationToken cancellationToken)
        {
            var endpoint = _context.CodePackageActivationContext.GetEndpoint("ServiceEndpoint");
            string url = $"http://+:{endpoint.Port}/";

            _listener = new HttpListener();
            _listener.Prefixes.Add(url);
            _listener.Start();

            Task.Run(() => HandleRequests(cancellationToken));
            return Task.FromResult(url.Replace("+", "localhost"));
        }

        private async Task HandleRequests(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var ctx = await _listener.GetContextAsync();
                using var reader = new StreamReader(ctx.Request.InputStream);
                var json = await reader.ReadToEndAsync();
                var order = JsonSerializer.Deserialize<BookOrder>(json);
                var result = _processRequest(order);
                var response = JsonSerializer.Serialize(result);
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
