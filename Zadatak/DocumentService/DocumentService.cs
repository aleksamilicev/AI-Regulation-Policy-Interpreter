using System;
using System.Collections.Generic;
using System.Fabric;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
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
            return new ServiceReplicaListener[]
            {
                new ServiceReplicaListener(serviceContext =>
                    new KestrelCommunicationListener(serviceContext, (url, listener) =>
                    {
                        return new WebHostBuilder()
                            .UseKestrel()
                            .ConfigureServices(services =>
                            {
                                services.AddSingleton<StatefulServiceContext>(serviceContext);
                                services.AddSingleton<IReliableStateManager>(this.StateManager);
                            })
                            .UseContentRoot(Directory.GetCurrentDirectory())
                            .UseStartup<Startup>()
                            .UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.UseUniqueServiceUrl)
                            .UseUrls(url)
                            .Build();
                    }))
            };
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            // Create upload folder
            var uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploaded-docs");
            if (!Directory.Exists(uploadFolder))
            {
                Directory.CreateDirectory(uploadFolder);
            }

            await base.RunAsync(cancellationToken);
        }
    }
}