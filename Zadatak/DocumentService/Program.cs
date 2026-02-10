using System;
using System.Threading;
using Microsoft.ServiceFabric.Services.Runtime;

namespace DocumentService
{
    internal static class Program
    {
        private static void Main()
        {
            try
            {
                ServiceRuntime.RegisterServiceAsync("DocumentServiceType",
                    context => new DocumentService(context)).GetAwaiter().GetResult();

                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}