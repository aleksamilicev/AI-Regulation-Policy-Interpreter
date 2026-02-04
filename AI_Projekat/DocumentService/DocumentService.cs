using System;
using System.Collections.Generic;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using DocumentService.Models;

namespace DocumentService
{
    /// <summary>
    /// An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    internal sealed class DocumentService : StatefulService
    {
        public DocumentService(StatefulServiceContext context)
            : base(context)
        { }

        public IReliableStateManager ReliableStateManager => this.StateManager;

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
                            .UseContentRoot(System.IO.Directory.GetCurrentDirectory())
                            .UseStartup<Startup>()
                            .UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.UseUniqueServiceUrl)
                            .UseUrls(url)
                            .Build();
                    }))
            };
        }
    }
}
