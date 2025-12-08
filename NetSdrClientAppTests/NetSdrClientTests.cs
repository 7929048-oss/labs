using Moq;
using NetSdrClientApp;
using NetSdrClientApp.Networking;
using System.Reflection;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NetSdrClientAppTests;

public class NetSdrClientTests
{
    NetSdrClient _client;
    Mock<ITcpClient> _tcpMock;
    Mock<IUdpClient> _udpMock;
    private readonly string _testFilePath = "samples.bin";

    public NetSdrClientTests() { }

    [SetUp]
    public void Setup()
    {
        _tcpMock = new Mock<ITcpClient>();
        _tcpMock.Setup(tcp => tcp.Connect()).Callback(() =>
        {
            _tcpMock.Setup(tcp => tcp.Connected).Returns(true);
        });

        _tcpMock.Setup(tcp => tcp.Disconnect()).Callback(() =>
        {
            _tcpMock.Setup(tcp => tcp.Connected).Returns(false);
        });

        _tcpMock.Setup(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>())).Callback<byte[]>((bytes) =>
        {
            _tcpMock.Raise(tcp => tcp.MessageReceived += null, _tcpMock.Object, bytes);
        });

        _udpMock = new Mock<IUdpClient>();

        _client = new NetSdrClient(_tcpMock.Object, _udpMock.Object);
        
        // Clean up test file before each test
        if (File.Exists(_testFilePath))
        {
            File.Delete(_testFilePath);
        }
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up test file after each test
        if (File.Exists(_testFilePath))
        {
            File.Delete(_testFilePath);
        }
    }

    [Test]
    public async Task ConnectAsyncTest()
    {
        //act
        await _client.ConnectAsync();

        //assert
        _tcpMock.Verify(tcp => tcp.Connect(), Times.Once);
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Exactly(3));
    }

    [Test]
    public void DisconnectWithNoConnectionTest()
    {
        //act
        _client.Disconect();

        //assert
        //No exception thrown
        _tcpMock.Verify(tcp => tcp.Disconnect(), Times.Once);
    }

    [Test]
    public async Task DisconnectTest()
    {
        //Arrange 
        await ConnectAsyncTest();

        //act
        _client.Disconect();

        //assert
        //No exception thrown
        _tcpMock.Verify(tcp => tcp.Disconnect(), Times.Once);
    }

    [Test]
    public async Task StartIQNoConnectionTest()
    {
        //act
        await _client.StartIQAsync();

        //assert
        //No exception thrown
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
        _tcpMock.VerifyGet(tcp => tcp.Connected, Times.AtLeastOnce);
    }

    [Test]
    public async Task StartIQTest()
    {
        //Arrange 
        await ConnectAsyncTest();

        //act
        await _client.StartIQAsync();

        //assert
        //No exception thrown
        _udpMock.Verify(udp => udp.StartListeningAsync(), Times.Once);
        Assert.That(_client.IQStarted, Is.True);
    }

    [Test]
    public async Task StopIQTest()
    {
        //Arrange 
        await ConnectAsyncTest();

        //act
        await _client.StopIQAsync();

        //assert
        //No exception thrown
        _udpMock.Verify(udp => udp.StopListening(), Times.Once);
        Assert.That(_client.IQStarted, Is.False);
    }

    [Test]
    public async Task ChangeFrequencyAsync_WhenConnected_SendsMessage()
    {
        // Arrange
        await ConnectAsyncTest();
        long frequency = 1000000;
        int channel = 1;

        // Act
        await _client.ChangeFrequencyAsync(frequency, channel);

        // Assert
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.AtLeast(4)); // 3 from ConnectAsync + 1 from ChangeFrequencyAsync
    }

    [Test]
    public async Task ChangeFrequencyAsync_WhenNotConnected_DoesNotSendMessage()
    {
        // Arrange
        long frequency = 1000000;
        int channel = 1;

        // Act
        await _client.ChangeFrequencyAsync(frequency, channel);

        // Assert
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
    }

    [Test]
    public void UdpMessageReceived_ProcessesMessageWithoutException()
    {
        // Arrange
        var testData = new byte[100]; // Minimal size for message processing
        for (int i = 0; i < testData.Length; i++)
        {
            testData[i] = (byte)(i % 256);
        }

        // Act & Assert - should not throw
        Assert.DoesNotThrow(() => 
            _udpMock.Raise(udp => udp.MessageReceived += null, _udpMock.Object, testData));
    }

    [Test]
    public void TcpMessageReceived_WithTaskCompletionSource_SetsResult()
    {
        // Arrange
        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        TaskCompletionSource<byte[]>? tcs = null;

        // Use reflection to access private field and method
        var field = typeof(NetSdrClient).GetField("_responseTaskSource", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        var method = typeof(NetSdrClient).GetMethod("_tcpClient_MessageReceived", 
            BindingFlags.NonPublic | BindingFlags.Instance);

        // Set up the TaskCompletionSource
        Assert.That(field, Is.Not.Null);
        field!.SetValue(_client, new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously));
        tcs = (TaskCompletionSource<byte[]>)field.GetValue(_client)!;

        // Act
        Assert.That(method, Is.Not.Null);
        if (method != null)
        {
            method.Invoke(_client, new object[] { _tcpMock.Object, testData });
        }

        // Assert
        Assert.That(tcs, Is.Not.Null);
        Assert.That(tcs.Task.IsCompleted, Is.True);
        Assert.That(tcs.Task.Result, Is.EqualTo(testData));
        Assert.That(field.GetValue(_client), Is.Null); // Should be cleared after setting result
    }

    [Test]
    public void TcpMessageReceived_WithoutTaskCompletionSource_DoesNotThrow()
    {
        // Arrange
        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var method = typeof(NetSdrClient).GetMethod("_tcpClient_MessageReceived", 
            BindingFlags.NonPublic | BindingFlags.Instance);

        // Act & Assert - should not throw even without TaskCompletionSource
        Assert.That(method, Is.Not.Null);
        if (method != null)
        {
            Assert.DoesNotThrow(() => 
                method.Invoke(_client, new object[] { _tcpMock.Object, testData }));
        }
    }

    [Test]
    public async Task SendTcpRequest_WhenConnected_ReturnsResponse()
    {
        // Arrange
        await ConnectAsyncTest();
        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var method = typeof(NetSdrClient).GetMethod("SendTcpRequest", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        // Set up TCP client to return test data
        _tcpMock.Setup(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()))
                .Callback<byte[]>(bytes => 
                    _tcpMock.Raise(tcp => tcp.MessageReceived += null, _tcpMock.Object, testData));

        // Act
        Assert.That(method, Is.Not.Null);
        var invokeResult = method!.Invoke(_client, new object[] { testData });
        Assert.That(invokeResult, Is.Not.Null);
        var task = (Task<byte[]>)invokeResult!;
        var result = await task;

        // Assert
    }

    [Test]
    public async Task SendTcpRequest_WhenNotConnected_ReturnsNull()
    {
        // Arrange
        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var method = typeof(NetSdrClient).GetMethod("SendTcpRequest", 
            BindingFlags.NonPublic | BindingFlags.Instance);

        // Act
        Assert.That(method, Is.Not.Null);
        var invokeResult = method!.Invoke(_client, new object[] { testData });
        Assert.That(invokeResult, Is.Not.Null);
        var task = (Task<byte[]>)invokeResult!;
        var result = await task;

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void IQStarted_Property_SetCorrectly()
    {
        // Arrange
        bool initialValue = _client.IQStarted;

        // Act
        _client.IQStarted = true;
        bool afterSetTrue = _client.IQStarted;
        
        _client.IQStarted = false;
        bool afterSetFalse = _client.IQStarted;

        // Assert
        Assert.That(initialValue, Is.False);
        Assert.That(afterSetTrue, Is.True);
        Assert.That(afterSetFalse, Is.False);
    }
}

// --- Appended networking and wrapper tests ---

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
                Assert.That(serverAccepted.Task.IsCompleted, Is.True, "Server did not accept connection in time");

                // Get accepted client and its stream
                var accepted = serverAccepted.Task.Result;
                using var serverStream = accepted.GetStream();

                // Verify wrapper reports connected
                Assert.That(wrapper.Connected, Is.True, "Wrapper should be connected after Connect()");

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
                Assert.That(serverReadTcs.Task.IsCompleted, Is.True, "Server did not receive message from wrapper");
                Assert.That(serverReadTcs.Task.Result, Is.EqualTo(payload));

                // Now test MessageReceived event triggered when server writes back
                var msgTcs = new TaskCompletionSource<byte[]>();
                wrapper.MessageReceived += (s, data) => msgTcs.TrySetResult(data);

                var response = Encoding.UTF8.GetBytes("pong-from-server");
                await serverStream.WriteAsync(response, 0, response.Length);

                var when = await Task.WhenAny(msgTcs.Task, Task.Delay(3000));
                Assert.That(msgTcs.Task.IsCompleted, Is.True, "Wrapper did not receive message from server");
                Assert.That(msgTcs.Task.Result, Is.EqualTo(response));
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

            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a.Equals(c), Is.False);
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

    [TestFixture]
    public class TcpClientWrapperTests
    {
        [Test]
        public void Connect_NoServer_DoesNotThrow_ConnectedFalse()
        {
            var wrapper = new TcpClientWrapper("127.0.0.1", 65000);
            Assert.DoesNotThrow(() => wrapper.Connect());
            Assert.That(wrapper.Connected, Is.False);
        }

        [Test]
        public void SendMessageAsync_NotConnected_ThrowsInvalidOperation()
        {
            var wrapper = new TcpClientWrapper("127.0.0.1", 65000);
            Assert.ThrowsAsync<InvalidOperationException>(async () => await wrapper.SendMessageAsync(new byte[] { 1 }));
        }

        [Test]
        public void SendMessageAsync_String_NotConnected_ThrowsInvalidOperation()
        {
            var wrapper = new TcpClientWrapper("127.0.0.1", 65000);
            Assert.ThrowsAsync<InvalidOperationException>(async () => await wrapper.SendMessageAsync("hello"));
        }

        [Test]
        public async Task Connect_SendReceive_EventRaised()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var acceptTask = Task.Run(async () => await listener.AcceptTcpClientAsync());

            var wrapper = new TcpClientWrapper("127.0.0.1", port);

            try
            {
                wrapper.Connect();
                var serverClient = await acceptTask;
                using var serverStream = serverClient.GetStream();

                Assert.That(wrapper.Connected, Is.True);

                var serverReadTcs = new TaskCompletionSource<byte[]>();
                _ = Task.Run(async () =>
                {
                    var buffer = new byte[1024];
                    int read = await serverStream.ReadAsync(buffer, 0, buffer.Length);
                    var arr = new byte[read];
                    Array.Copy(buffer, arr, read);
                    serverReadTcs.SetResult(arr);
                });

                var payload = Encoding.UTF8.GetBytes("ping");
                await wrapper.SendMessageAsync(payload);

                var completed = await Task.WhenAny(serverReadTcs.Task, Task.Delay(3000));
                Assert.That(serverReadTcs.Task.IsCompleted, Is.True, "Server did not receive message from wrapper");
                Assert.That(serverReadTcs.Task.Result, Is.EqualTo(payload));

                var msgTcs = new TaskCompletionSource<byte[]>();
                wrapper.MessageReceived += (s, e) => msgTcs.TrySetResult(e);

                var response = Encoding.UTF8.GetBytes("pong");
                await serverStream.WriteAsync(response, 0, response.Length);

                var when = await Task.WhenAny(msgTcs.Task, Task.Delay(3000));
                Assert.That(msgTcs.Task.IsCompleted, Is.True, "Wrapper did not receive message from server");
                Assert.That(msgTcs.Task.Result, Is.EqualTo(response));
            }
            finally
            {
                wrapper.Disconnect();
                listener.Stop();
            }
        }
    }

    [TestFixture]
    public class UdpClientWrapperTests
    {
        [Test]
        public async Task StartListeningAsync_ReceivesMessage_MessageRaised()
        {
            // pick an available UDP port
            int port;
            using (var temp = new UdpClient(0))
            {
                port = ((IPEndPoint)temp.Client.LocalEndPoint!).Port;
            }

            var wrapper = new UdpClientWrapper(port);

            try
            {
                var msgTcs = new TaskCompletionSource<byte[]>();
                wrapper.MessageReceived += (s, data) => msgTcs.TrySetResult(data);

                // start listening in background
                var listenTask = Task.Run(() => wrapper.StartListeningAsync());

                // give it a moment to bind
                await Task.Delay(100);

                using var sender = new UdpClient();
                var payload = Encoding.UTF8.GetBytes("udp-test-payload");
                await sender.SendAsync(payload, payload.Length, IPAddress.Loopback.ToString(), port);

                var completed = await Task.WhenAny(msgTcs.Task, Task.Delay(3000));
                Assert.That(msgTcs.Task.IsCompleted, Is.True, "Did not receive UDP message in time");
                Assert.That(msgTcs.Task.Result, Is.EqualTo(payload));

                // stop listening cleanly
                wrapper.StopListening();
                await Task.Delay(50);
            }
            finally
            {
                wrapper.Dispose();
            }
        }

        [Test]
        public void StopListening_BeforeStart_DoesNotThrow()
        {
            int port;
            using (var temp = new UdpClient(0))
            {
                port = ((IPEndPoint)temp.Client.LocalEndPoint!).Port;
            }

            var wrapper = new UdpClientWrapper(port);
            Assert.DoesNotThrow(() => wrapper.StopListening());
            wrapper.Dispose();
        }

        [Test]
        public void Dispose_CanBeCalledMultipleTimes_NoThrow()
        {
            int port;
            using (var temp = new UdpClient(0))
            {
                port = ((IPEndPoint)temp.Client.LocalEndPoint!).Port;
            }

            var wrapper = new UdpClientWrapper(port);
            Assert.DoesNotThrow(() => wrapper.Dispose());
            Assert.DoesNotThrow(() => wrapper.Dispose());
        }

        [Test]
        public void EqualsAndGetHashCode_Consistency()
        {
            int port;
            using (var temp = new UdpClient(0))
            {
                port = ((IPEndPoint)temp.Client.LocalEndPoint!).Port;
            }

            var a = new UdpClientWrapper(port);
            var b = new UdpClientWrapper(port);
            var c = new UdpClientWrapper(port + 1);

            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a.Equals(c), Is.False);

            a.Dispose();
            b.Dispose();
            c.Dispose();
        }
    }