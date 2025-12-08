using NetSdrClientApp.Messages;
using NUnit.Framework;

namespace NetSdrClientAppTests
{
    public class NetSdrMessageHelperTests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void GetControlItemMessageTest()
        {
            //Arrange
            var type = NetSdrMessageHelper.MsgTypes.Ack;
            var code = NetSdrMessageHelper.ControlItemCodes.ReceiverState;
            int parametersLength = 7500;

            //Act
            byte[] msg = NetSdrMessageHelper.GetControlItemMessage(type, code, new byte[parametersLength]);

            var headerBytes = msg.Take(2);
            var codeBytes = msg.Skip(2).Take(2);
            var parametersBytes = msg.Skip(4);

            var num = BitConverter.ToUInt16(headerBytes.ToArray());
            var actualType = (NetSdrMessageHelper.MsgTypes)(num >> 13);
            var actualLength = num - ((int)actualType << 13);
            var actualCode = BitConverter.ToInt16(codeBytes.ToArray());

            //Assert
            Assert.That(headerBytes.Count(), Is.EqualTo(2));
            Assert.That(msg.Length, Is.EqualTo(actualLength));
            Assert.That(type, Is.EqualTo(actualType));

            Assert.That(actualCode, Is.EqualTo((short)code));

            Assert.That(parametersBytes.Count(), Is.EqualTo(parametersLength));
        }

        [Test]
        public void GetDataItemMessageTest()
        {
            //Arrange
            var type = NetSdrMessageHelper.MsgTypes.DataItem2;
            int parametersLength = 7500;

            //Act
            byte[] msg = NetSdrMessageHelper.GetDataItemMessage(type, new byte[parametersLength]);

            var headerBytes = msg.Take(2);
            var parametersBytes = msg.Skip(2);

            var num = BitConverter.ToUInt16(headerBytes.ToArray());
            var actualType = (NetSdrMessageHelper.MsgTypes)(num >> 13);
            var actualLength = num - ((int)actualType << 13);

            //Assert
            Assert.That(headerBytes.Count(), Is.EqualTo(2));
            Assert.That(msg.Length, Is.EqualTo(actualLength));
            Assert.That(type, Is.EqualTo(actualType));

            Assert.That(parametersBytes.Count(), Is.EqualTo(parametersLength));
        }
        
        [Test]
        public void TranslateMessage_ControlItem_ValidCode_ParsesCorrectly()
        {
            var type = NetSdrMessageHelper.MsgTypes.SetControlItem;
            var code = NetSdrMessageHelper.ControlItemCodes.ReceiverFrequency;
            byte[] parameters = { 0x01, 0x02, 0x03 };

            var msg = NetSdrMessageHelper.GetControlItemMessage(type, code, parameters);

            var success = NetSdrMessageHelper.TranslateMessage(
                msg,
                out var actualType,
                out var actualCode,
                out var sequenceNumber,
                out var body);
            
            Assert.That(success, Is.True);
            Assert.That(actualType, Is.EqualTo(type));
            Assert.That(actualCode, Is.EqualTo(code));
            Assert.That(sequenceNumber, Is.EqualTo(0));
            Assert.That(body, Is.EqualTo(parameters));
        }

        [Test]
        public void TranslateMessage_ControlItem_InvalidCode_ReturnsFalseAndItemCodeNone()
        {
            var type = NetSdrMessageHelper.MsgTypes.SetControlItem;
            ushort lengthWithHeader = 6;
            ushort headerValue = (ushort)(lengthWithHeader + ((int)type << 13));
            byte[] headerBytes = BitConverter.GetBytes(headerValue);

            ushort invalidCodeValue = 0xFFFF;
            byte[] itemCodeBytes = BitConverter.GetBytes(invalidCodeValue);
            byte[] parameters = { 0x10, 0x20 };

            var msg = headerBytes
                .Concat(itemCodeBytes)
                .Concat(parameters)
                .ToArray();
            
            var success = NetSdrMessageHelper.TranslateMessage(
                msg,
                out var actualType,
                out var actualCode,
                out var sequenceNumber,
                out var body);
            
            Assert.That(success, Is.False);
            Assert.That(actualType, Is.EqualTo(type));
            Assert.That(actualCode, Is.EqualTo(NetSdrMessageHelper.ControlItemCodes.None));
            Assert.That(sequenceNumber, Is.EqualTo(0));
            Assert.That(body, Is.EqualTo(parameters));
        }

        [Test]
        public void TranslateMessage_DataItem_ValidSequenceAndBody()
        {
            var type = NetSdrMessageHelper.MsgTypes.DataItem1;
            ushort sequenceNumber = 0x1234;
            byte[] sequenceBytes = BitConverter.GetBytes(sequenceNumber);
            byte[] payload = { 0x10, 0x20, 0x30, 0x40 };
            
            byte[] parameters = sequenceBytes.Concat(payload).ToArray();

            var msg = NetSdrMessageHelper.GetDataItemMessage(type, parameters);

            var success = NetSdrMessageHelper.TranslateMessage(
                msg,
                out var actualType,
                out var actualCode,
                out var actualSequenceNumber,
                out var body);
            
            Assert.That(success, Is.True);
            Assert.That(actualType, Is.EqualTo(type));
            Assert.That(actualCode, Is.EqualTo(NetSdrMessageHelper.ControlItemCodes.None));
            Assert.That(actualSequenceNumber, Is.EqualTo(sequenceNumber));
            Assert.That(body, Is.EqualTo(payload));
        }

        [Test]
        public void TranslateMessage_DataItem_MaxLengthHeaderZero_DecodedCorrectly()
        {
            var type = NetSdrMessageHelper.MsgTypes.DataItem0;
            ushort sequenceNumber = 0x4321;
            byte[] sequenceBytes = BitConverter.GetBytes(sequenceNumber);
            byte[] payload = new byte[8190]; 

            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(i % 256);
            }

            byte[] body = sequenceBytes.Concat(payload).ToArray(); 

            ushort headerValue = (ushort)((int)type << 13);
            byte[] headerBytes = BitConverter.GetBytes(headerValue);

            byte[] msg = headerBytes.Concat(body).ToArray();

            var success = NetSdrMessageHelper.TranslateMessage(
                msg,
                out var actualType,
                out var actualCode,
                out var actualSequenceNumber,
                out var actualBody);

            Assert.That(success, Is.True);
            Assert.That(actualType, Is.EqualTo(type));
            Assert.That(actualCode, Is.EqualTo(NetSdrMessageHelper.ControlItemCodes.None));
            Assert.That(actualSequenceNumber, Is.EqualTo(sequenceNumber));
            Assert.That(actualBody.Length, Is.EqualTo(payload.Length));
            Assert.That(actualBody, Is.EqualTo(payload));
        }

        [Test]
        public void TranslateMessage_BodyLengthMismatch_ReturnsFalse()
        {
            var type = NetSdrMessageHelper.MsgTypes.DataItem2;
            ushort sequenceNumber = 1;
            byte[] sequenceBytes = BitConverter.GetBytes(sequenceNumber);
            byte[] payload = { 0x01, 0x02, 0x03, 0x04 };

            byte[] parameters = sequenceBytes.Concat(payload).ToArray();
            byte[] msg = NetSdrMessageHelper.GetDataItemMessage(type, parameters);
            
            msg = msg.Take(msg.Length - 1).ToArray();

            var success = NetSdrMessageHelper.TranslateMessage(
                msg,
                out _,
                out _,
                out _,
                out _);

            Assert.That(success, Is.False);
        }

        [Test]
        public void GetControlItemMessage_TooLongParameters_ThrowsArgumentException()
        {
            var type = NetSdrMessageHelper.MsgTypes.SetControlItem;
            var code = NetSdrMessageHelper.ControlItemCodes.ReceiverState;
            
            int parametersLength = 8190;
            
            Assert.Throws<ArgumentException>(() =>
                NetSdrMessageHelper.GetControlItemMessage(type, code, new byte[parametersLength]));
        }

        [Test]
        public void GetDataItemMessage_MaxLength_UsesZeroLengthInHeader()
        {
            var type = NetSdrMessageHelper.MsgTypes.DataItem2;
            int parametersLength = 8192;

            byte[] parameters = new byte[parametersLength];
            
            var msg = NetSdrMessageHelper.GetDataItemMessage(type, parameters);

            var headerBytes = msg.Take(2).ToArray();
            ushort header = BitConverter.ToUInt16(headerBytes);
            var actualType = (NetSdrMessageHelper.MsgTypes)(header >> 13);
            var encodedLengthWithHeader = header - ((int)actualType << 13);
            
            Assert.That(actualType, Is.EqualTo(type));
            Assert.That(encodedLengthWithHeader, Is.EqualTo(0));     
            Assert.That(msg.Length, Is.EqualTo(8194));               
        }

        [Test]
        public void GetSamples_TooLargeSampleSize_Throws()
        {
            ushort sampleSize = 40;
            byte[] body = new byte[10];

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                NetSdrMessageHelper.GetSamples(sampleSize, body).ToArray());
        }

        [Test]
        public void GetSamples_NullBody_Throws()
        {
            ushort sampleSize = 8;

            Assert.Throws<ArgumentNullException>(() =>
                NetSdrMessageHelper.GetSamples(sampleSize, null!).ToArray());
        }

        [Test]
        public void GetSamples_8BitSamples_ReturnsExpectedValues()
        {
            ushort sampleSize = 8;
            byte[] body = { 1, 2, 3 };

            var samples = NetSdrMessageHelper.GetSamples(sampleSize, body).ToArray();

            Assert.That(samples, Is.EqualTo(new[] { 1, 2, 3 }).AsCollection);
        }

        [Test]
        public void GetSamples_16BitSamples_IgnoresTrailingIncompleteSample()
        {
            ushort sampleSize = 16;
            byte[] body = { 1, 0, 2, 0, 3 };

            var samples = NetSdrMessageHelper.GetSamples(sampleSize, body).ToArray();

            Assert.That(samples, Is.EqualTo(new[] { 1, 2 }).AsCollection);
        }

        //TODO: add more NetSdrMessageHelper tests
    }
}