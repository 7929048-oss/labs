using System;
using System.Text;
using System.Threading.Tasks;

namespace NetSdrClientApp
{
    internal static class Program
    {
        // Minimal, safe example using NetSdrClient and demonstrating proper disposal and exception handling.
        private static async Task<int> Main(string[] args)
        {
            // Simple argument parsing with null/empty checks avoids potential NREs
            var host = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : "localhost";
            var port = 12345;

            try
            {
                await using var client = new NetSdrClient();
                await client.ConnectAsync(host, port);

                var payload = Encoding.UTF8.GetBytes("hello");
                await client.SendMessageAsync(payload);

                client.Disconnect();
                return 0;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Operation was canceled.");
                return 2;
            }
            catch (Exception ex)
            {
                // Avoid empty catch — write meaningful error for diagnostics
                Console.Error.WriteLine($"Unexpected error: {ex.Message}");
                return 1;
            }
        }
    }
}
