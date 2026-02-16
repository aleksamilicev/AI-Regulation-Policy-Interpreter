using System;
using System.Collections.Generic;
using System.Fabric;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.AspNetCore.Hosting;

namespace DocumentService
{
    internal sealed class DocumentService : StatefulService
    {
        public DocumentService(StatefulServiceContext context)
            : base(context)
        { }

        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new[]
            {
                new ServiceReplicaListener(serviceContext =>
                    new KestrelCommunicationListener(
                        serviceContext,
                        "ServiceEndpoint",
                        (url, listener) =>
                        {
                            ServiceEventSource.Current.ServiceMessage(serviceContext, $"Starting Kestrel on {url}");
                            return new WebHostBuilder()
                                .UseKestrel()
                                .ConfigureServices(services =>
                                {
                                    services.AddSingleton<StatefulServiceContext>(serviceContext);
                                    services.AddSingleton<IReliableStateManager>(this.StateManager);
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

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            var storageRoot = Path.Combine(Directory.GetCurrentDirectory(), "storage");
            var documentsFolder = Path.Combine(storageRoot, "documents");
            var parsedFolder = Path.Combine(storageRoot, "parsed");
            var embeddingsFolder = Path.Combine(storageRoot, "embeddings");

            Directory.CreateDirectory(documentsFolder);
            Directory.CreateDirectory(parsedFolder);
            Directory.CreateDirectory(embeddingsFolder);

            ServiceEventSource.Current.ServiceMessage(this.Context, $"Storage folders created at: {storageRoot}");

            await base.RunAsync(cancellationToken);
        }
    }
}