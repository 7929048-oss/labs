using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetSdrClientApp.Networking
{
    public class TcpClientWrapper : ITcpClient
    {
        private readonly string _host;
        private readonly int _port;
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;

        public bool Connected => _tcpClient != null && _tcpClient.Connected && _stream != null;

        public event EventHandler<byte[]>? MessageReceived;

        public TcpClientWrapper(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public void Connect()
        {
            if (Connected)
            {
                System.Diagnostics.Debug.WriteLine($"Already connected to {_host}:{_port}");
                return;
            }

            _tcpClient = new TcpClient();
            try
            {
                _cts = new CancellationTokenSource();
                _tcpClient.Connect(_host, _port);
                _stream = _tcpClient.GetStream();
                System.Diagnostics.Debug.WriteLine($"Connected to {_host}:{_port}");
                _ = StartListeningAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to connect: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            if (Connected)
            {
                _cts?.Cancel();
                _stream?.Close();
                _tcpClient?.Close();
                _cts?.Dispose();
                _cts = null;
                _tcpClient = null;
                _stream = null;
                Console.WriteLine("Disconnected.");
            }
            else
            {
                // Avoid noisy console output in tests when Disconnect is called without an active connection.
                System.Diagnostics.Debug.WriteLine("Disconnect called with no active connection.");
            }
        }

        // Generalized send message method to handle both byte[] and string
        private async Task SendMessageAsyncInternal(byte[] data)
        {
            if (Connected && _stream != null && _stream.CanWrite)
            {
                System.Diagnostics.Debug.WriteLine($"Message sent: " + data.Select(b => Convert.ToString(b, toBase: 16)).Aggregate((l, r) => $"{l} {r}"));
                await _stream.WriteAsync(data, 0, data.Length);
            }
            else
            {
                throw new InvalidOperationException("Not connected to a server.");
            }
        }

        public async Task SendMessageAsync(byte[] data)
        {
            await SendMessageAsyncInternal(data); 
        }

        public async Task SendMessageAsync(string str)
        {
            var data = Encoding.UTF8.GetBytes(str);
            await SendMessageAsyncInternal(data); 
        }

        private async Task StartListeningAsync()
        {
            if (Connected && _stream != null && _stream.CanRead && _cts != null)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"Starting listening for incoming messages.");
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        byte[] buffer = new byte[8194];
                        int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                        if (bytesRead > 0)
                        {
                            MessageReceived?.Invoke(this, buffer.AsSpan(0, bytesRead).ToArray());
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    //empty
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in listening loop: {ex.Message}");
                }
                finally
                {
                    System.Diagnostics.Debug.WriteLine("Listener stopped.");
                }
            }
            else
            {
                throw new InvalidOperationException("Not connected to a server.");
            }
        }
    }
}
