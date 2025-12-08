using NUnit.Framework;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetSdrClientApp.Networking;

namespace NetSdrClientAppTests
{
    [TestFixture]
    public class NetSdrClientNetworkingTests
    {
        [Test]
        public async Task TcpClientWrapper_Connect_Send_Receive_Roundtrip()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var serverAccepted = new TaskCompletionSource<TcpClient>();

            // Accept client in background
            var acceptTask = Task.Run(async () =>
            {
                var client = await listener.AcceptTcpClientAsync();
                serverAccepted.SetResult(client);
            });

            var wrapper = new TcpClientWrapper("127.0.0.1", port);

            try
            {
                wrapper.Connect();

                // wait for server accept
                var serverClient = await Task.WhenAny(serverAccepted.Task, Task.Delay(5000));
                Assert.IsTrue(serverAccepted.Task.IsCompleted, "Server did not accept connection in time");

                // Get accepted client and its stream
                var accepted = serverAccepted.Task.Result;
                using var serverStream = accepted.GetStream();

                // Verify wrapper reports connected
                Assert.IsTrue(wrapper.Connected, "Wrapper should be connected after Connect()");

                // Prepare to receive message at server side
                var serverReadTcs = new TaskCompletionSource<byte[]>();
                var serverReadTask = Task.Run(async () =>
                {
                    var buffer = new byte[1024];
                    int read = await serverStream.ReadAsync(buffer, 0, buffer.Length);
                    var arr = new byte[read];
                    Array.Copy(buffer, arr, read);
                    serverReadTcs.SetResult(arr);
                });

                // Send from client (wrapper)
                var payload = Encoding.UTF8.GetBytes("hello-from-wrapper");
                await wrapper.SendMessageAsync(payload);

                var completed = await Task.WhenAny(serverReadTcs.Task, Task.Delay(3000));
                Assert.IsTrue(serverReadTcs.Task.IsCompleted, "Server did not receive message from wrapper");
                CollectionAssert.AreEqual(payload, serverReadTcs.Task.Result);

                // Now test MessageReceived event triggered when server writes back
                var msgTcs = new TaskCompletionSource<byte[]>();
                wrapper.MessageReceived += (s, data) => msgTcs.TrySetResult(data);

                var response = Encoding.UTF8.GetBytes("pong-from-server");
                await serverStream.WriteAsync(response, 0, response.Length);

                var when = await Task.WhenAny(msgTcs.Task, Task.Delay(3000));
                Assert.IsTrue(msgTcs.Task.IsCompleted, "Wrapper did not receive message from server");
                CollectionAssert.AreEqual(response, msgTcs.Task.Result);
            }
            finally
            {
                wrapper.Disconnect();
                listener.Stop();
            }
        }

        [Test]
        public void TcpClientWrapper_Disconnect_WhenNotConnected_DoesNotThrow()
        {
            var wrapper = new TcpClientWrapper("127.0.0.1", 65000);
            // Should not throw even if not connected
            Assert.DoesNotThrow(() => wrapper.Disconnect());
        }

        [Test]
        public void UdpClientWrapper_Equals_And_GetHashCode()
        {
            // Get two available UDP ports by temporarily binding
            int port1;
            using (var temp = new UdpClient(0))
            {
                port1 = ((IPEndPoint)temp.Client.LocalEndPoint!).Port;
            }

            int port2;
            using (var temp = new UdpClient(0))
            {
                port2 = ((IPEndPoint)temp.Client.LocalEndPoint!).Port;
            }

            var a = new UdpClientWrapper(port1);
            var b = new UdpClientWrapper(port1);
            var c = new UdpClientWrapper(port2);

            Assert.IsTrue(a.Equals(b));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.IsFalse(a.Equals(c));
        }

        [Test]
        public void UdpClientWrapper_Dispose_StopListening_NoThrow()
        {
            int port;
            using (var temp = new UdpClient(0))
            {
                port = ((IPEndPoint)temp.Client.LocalEndPoint!).Port;
            }

            var wrapper = new UdpClientWrapper(port);
            Assert.DoesNotThrow(() => wrapper.StopListening());
            Assert.DoesNotThrow(() => wrapper.Dispose());
        }
    }
}
