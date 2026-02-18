using System;
using System.Threading;
using Microsoft.ServiceFabric.Services.Runtime;

namespace RetrievalService
{
    internal static class Program
    {
        private static void Main()
        {
            try
            {
                ServiceRuntime.RegisterServiceAsync("RetrievalServiceType",
                    context => new RetrievalService(context)).GetAwaiter().GetResult();

                ServiceEventSource.Current.ServiceTypeRegistered(
                    System.Diagnostics.Process.GetCurrentProcess().Id,
                    typeof(RetrievalService).Name);

                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceHostInitializationFailed(e.ToString());
                throw;
            }
        }
    }
}