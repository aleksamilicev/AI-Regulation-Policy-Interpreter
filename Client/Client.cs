using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using System.Collections.Generic;
using System.Fabric;

namespace Client
{
    public class Client : StatelessService
    {
        public Client(StatelessServiceContext context) : base(context) { }

        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new[]
            {
                new ServiceInstanceListener(serviceContext =>
                    new KestrelCommunicationListener(
                        serviceContext,
                        "ClientEndpoint",
                        (url, listener) =>
                        {
                            return new WebHostBuilder()
                                .UseKestrel()
                                .ConfigureServices(services =>
                                {
                                    services.AddSingleton(serviceContext);
                                    services.AddControllersWithViews(); // omogućava MVC + Razor
                                })
                                .Configure(app =>
                                {
                                    app.UseRouting();
                                    app.UseStaticFiles();

                                    app.UseEndpoints(endpoints =>
                                    {
                                        // default route
                                        endpoints.MapControllerRoute(
                                            name: "default",
                                            pattern: "{controller=Home}/{action=Index}/{id?}");
                                    });
                                })
                                .UseUrls(url)
                                .Build();
                        }))
            };
        }
    }
}
