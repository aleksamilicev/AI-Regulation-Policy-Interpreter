using System;
using System.Collections.Generic;
using System.Fabric;
using System.IO;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using LLMService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LLMService
{
    internal sealed class LLMService : StatelessService
    {
        public LLMService(StatelessServiceContext context)
            : base(context)
        { }

        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new[]
            {
                new ServiceInstanceListener(serviceContext =>
                    new KestrelCommunicationListener(
                        serviceContext,
                        "ServiceEndpoint",
                        (url, listener) =>
                        {
                            ServiceEventSource.Current.ServiceMessage(serviceContext, $"Starting LLM Service on {url}");

                            return new WebHostBuilder()
                                .UseKestrel()
                                .ConfigureServices(services =>
                                {
                                    services.AddSingleton<StatelessServiceContext>(serviceContext);
                                    services.AddSingleton<OllamaService>();
                                    services.AddControllers();
                                })
                                .UseContentRoot(Directory.GetCurrentDirectory())
                                .UseStartup<Startup>()
                                .UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.None)
                                .UseUrls(url)
                                .Build();
                        }))
            };
        }
    }
}