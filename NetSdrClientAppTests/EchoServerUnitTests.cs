using NUnit.Framework;
using System;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using EchoServer;

namespace EchoTcpServerTests
{
    [TestFixture]
    public class EchoServerUnitTests
    {
        [Test]
        public async Task StartAndStop_Server_CanStartOnEphemeralPort()
        {
            var server = new EchoServer.EchoServer(0);

            var serverTask = server.StartAsync();

            // wait until server reports a listening port
            for (int i = 0; i < 20 && server.ListeningPort == 0; i++)
            {
                await Task.Delay(50);
            }

            Assert.That(server.ListeningPort, Is.GreaterThan(0));

            server.Stop();

            // allow server to exit
            await Task.Delay(50);
        }

        [Test]
        public async Task EchoBehavior_EchosSentBytes()
        {
            var server = new EchoServer.EchoServer(0);
            var serverTask = server.StartAsync();

            // wait for port
            for (int i = 0; i < 20 && server.ListeningPort == 0; i++) await Task.Delay(50);
            int port = server.ListeningPort;

            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                var stream = client.GetStream();

                byte[] payload = Encoding.UTF8.GetBytes("hello-echo");
                await stream.WriteAsync(payload, 0, payload.Length);

                byte[] buffer = new byte[1024];
                var cts = new CancellationTokenSource(1000);
                int read = await stream.ReadAsync(buffer, 0, payload.Length, cts.Token);

                Assert.AreEqual(payload.Length, read);
                var received = Encoding.UTF8.GetString(buffer, 0, read);
                Assert.AreEqual("hello-echo", received);
            }

            server.Stop();
            await Task.Delay(50);
        }

        [Test]
        public async Task UdpTimedSender_SendsMessage_ReceivableByUdpClient()
        {
            int listenPort = 0;
            using (var receiver = new UdpClient(0))
            {
                listenPort = ((IPEndPoint)receiver.Client.LocalEndPoint).Port;

                using (var sender = new EchoServer.UdpTimedSender("127.0.0.1", listenPort))
                {
                    var got = new TaskCompletionSource<byte[]>();

                    var recvTask = Task.Run(async () =>
                    {
                        var res = await receiver.ReceiveAsync();
                        got.SetResult(res.Buffer);
                    });

                    sender.StartSending(200);

                    var buffer = await Task.WhenAny(got.Task, Task.Delay(2000));
                    sender.StopSending();

                    Assert.IsTrue(got.Task.IsCompleted, "Did not receive UDP message from sender");
                }
            }
        }

        [Test]
        public async Task Stop_WhenNoClientConnected_DoesNotThrow()
        {
            var server = new EchoServer.EchoServer(0);
            var t = server.StartAsync();

            for (int i = 0; i < 20 && server.ListeningPort == 0; i++) await Task.Delay(50);

            Assert.That(server.ListeningPort, Is.GreaterThan(0));

            // stop without any client connected
            Assert.DoesNotThrow(() => server.Stop());

            await Task.Delay(50);
        }
    }
}
