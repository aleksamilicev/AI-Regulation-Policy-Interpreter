using Microsoft.AspNetCore.Hosting;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using RetrievalService.Services;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RetrievalService
{
    internal sealed class RetrievalService : StatelessService
    {
        public RetrievalService(StatelessServiceContext context)
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
                    // Uèitaj storage path iz config-a
                    var configPackage = serviceContext.CodePackageActivationContext.GetConfigurationPackageObject("Config");
                    var storageRoot = configPackage.Settings.Sections["StorageConfig"].Parameters["StorageRootPath"].Value;

                    return new WebHostBuilder()
                        .UseKestrel()
                        .ConfigureServices(services =>
                        {
                            services.AddSingleton<StatelessServiceContext>(serviceContext);
                            services.AddSingleton(new VectorSearchService(storageRoot)); // Prosledi path
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