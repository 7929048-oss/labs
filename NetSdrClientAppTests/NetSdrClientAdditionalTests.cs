using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NetSdrClientApp;
using NetSdrClientApp.Messages;
using NetSdrClientApp.Networking;
using NUnit.Framework;

namespace NetSdrClientAppTests
{
    [TestFixture]
    public class NetSdrClientAdditionalTests
    {
        [Test]
        public void NetSdrMessageHelper_Header_Roundtrip()
        {
            var parameters = new byte[] { 1, 2, 3, 4 };
            var msg = NetSdrMessageHelper.GetControlItemMessage(NetSdrMessageHelper.MsgTypes.SetControlItem, NetSdrMessageHelper.ControlItemCodes.IQOutputDataSampleRate, parameters);

            bool ok = NetSdrMessageHelper.TranslateMessage(msg, out var type, out var code, out ushort seq, out var body);

            Assert.IsTrue(ok);
            Assert.AreEqual(NetSdrMessageHelper.MsgTypes.SetControlItem, type);
            Assert.AreEqual(NetSdrMessageHelper.ControlItemCodes.IQOutputDataSampleRate, code);
            Assert.AreEqual(parameters.Length, body.Length);
            CollectionAssert.AreEqual(parameters, body);
        }

        [Test]
        public void NetSdrMessageHelper_GetSamples_ValidAndInvalid()
        {
            var body = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var samples = NetSdrMessageHelper.GetSamples(16, body).ToArray();
            Assert.AreEqual(2, samples.Length);

            // invalid sample size (>32 bits) -> should throw
            Assert.Throws<ArgumentOutOfRangeException>(() => NetSdrMessageHelper.GetSamples(40, body).ToArray());
        }

        [Test]
        public async Task NetSdrClient_StartIQAsync_And_StopIQAsync_InvokeNetworking()
        {
            var tcpMock = new Mock<ITcpClient>();
            var udpMock = new Mock<IUdpClient>();

            tcpMock.SetupGet(t => t.Connected).Returns(true);
            tcpMock.Setup(t => t.SendMessageAsync(It.IsAny<byte[]>())).Returns(Task.CompletedTask)
                .Callback<byte[]>((msg) => tcpMock.Raise(m => m.MessageReceived += null, tcpMock.Object, new byte[] { 0, 1, 2 }));

            udpMock.Setup(u => u.StartListeningAsync()).Returns(Task.CompletedTask);

            var client = new NetSdrClient(tcpMock.Object, udpMock.Object);

            await client.StartIQAsync();

            Assert.IsTrue(client.IQStarted);
            tcpMock.Verify(t => t.SendMessageAsync(It.IsAny<byte[]>()), Times.AtLeastOnce);
            udpMock.Verify(u => u.StartListeningAsync(), Times.AtLeastOnce);

            // Now stop
            await client.StopIQAsync();
            Assert.IsFalse(client.IQStarted);
            udpMock.Verify(u => u.StopListening(), Times.AtLeastOnce);
        }

        [Test]
        public void TcpClientWrapper_SendMessage_ThrowsWhenNotConnected()
        {
            var wrapper = new NetSdrClientApp.Networking.TcpClientWrapper("127.0.0.1", 65000);

            Assert.ThrowsAsync<InvalidOperationException>(async () => await wrapper.SendMessageAsync(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void UdpClientWrapper_EqualsAndHashcode()
        {
            var a = new NetSdrClientApp.Networking.UdpClientWrapper(12345);
            var b = new NetSdrClientApp.Networking.UdpClientWrapper(12345);
            var c = new NetSdrClientApp.Networking.UdpClientWrapper(54321);

            Assert.IsTrue(a.Equals(b));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.IsFalse(a.Equals(c));
        }
    }
}
