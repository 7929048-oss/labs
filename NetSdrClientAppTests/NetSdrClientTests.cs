using Moq;
using NetSdrClientApp;
using NetSdrClientApp.Networking;
using System.Reflection;

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