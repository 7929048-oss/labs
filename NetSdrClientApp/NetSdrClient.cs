 url=https://github.com/7929048-oss/labs/blob/master/NetSdrClientApp/NetSdrClient.cs
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NetSdrClientApp
{
    /// <summary>
    /// Simple network client for tests/demo.
    /// Implements IDisposable to ensure TcpClient is properly disposed (fixes resource-leak warnings).
    /// Provides both the original (misspelled) Disconect for compatibility with existing tests
    /// and a correctly spelled Disconnect() which they both delegate to.
    /// </summary>
    public sealed class NetSdrClient : IDisposable
    {
        private TcpClient? _tcpClient;
        private readonly object _lock = new();
        private bool _disposed;

        // Example configurable timeout / constant instead of magic number
        private const int DefaultConnectTimeoutMs = 5000;

        public bool IsConnected => _tcpClient?.Connected ?? false;

        public NetSdrClient()
        {
        }

        /// <summary>
        /// Connects to the remote endpoint asynchronously.
        /// </summary>
        public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // Avoid re-creating connection concurrently
            lock (_lock)
            {
                if (_tcpClient != null && _tcpClient.Connected)
                    return;
                _tcpClient?.Dispose();
                _tcpClient = new TcpClient();
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(DefaultConnectTimeoutMs);

            try
            {
                await _tcpClient!.ConnectAsync(host, port).WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Preserve cancellation semantics — bubble up to caller
                throw;
            }
            catch (SocketException ex)
            {
                // Do not swallow exceptions — log or rethrow so Sonar doesn't flag empty catch
                throw new InvalidOperationException($"Failed to connect to {host}:{port}", ex);
            }
        }

        /// <summary>
        /// Sends a message asynchronously. Ensures network stream is available and checks for nulls.
        /// </summary>
        public async Task SendMessageAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            ThrowIfDisposed();

            var client = _tcpClient;
            if (client == null || !client.Connected)
                throw new InvalidOperationException("Client is not connected.");

            try
            {
                using var stream = client.GetStream();
                await stream.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ObjectDisposedException || ex is InvalidOperationException || ex is IOException)
            {
                // propagate useful context
                throw new InvalidOperationException("Failed to send message.", ex);
            }
        }

        /// <summary>
        /// Correctly spelled Disconnect. Disposes the underlying TcpClient.
        /// </summary>
        public void Disconnect()
        {
            if (_disposed) return;

            lock (_lock)
            {
                try
                {
                    _tcpClient?.Close();
                    _tcpClient?.Dispose();
                }
                finally
                {
                    _tcpClient = null;
                }
            }
        }

        /// <summary>
        /// Backwards-compatible misspelled method to preserve existing tests that call Disconect().
        /// For new code, use Disconnect().
        /// </summary>
        [Obsolete("Use Disconnect() instead.")]
        public void Disconect() => Disconnect();

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NetSdrClient));
        }

        public void Dispose()
        {
            if (_disposed) return;
            lock (_lock)
            {
                if (_disposed) return;
                try
                {
                    _tcpClient?.Close();
                    _tcpClient?.Dispose();
                    _tcpClient = null;
                }
                finally
                {
                    _disposed = true;
                }
            }

            GC.SuppressFinalize(this);
        }
    }
}
