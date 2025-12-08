// This file was a duplicate EchoServer test accidentally placed under NetSdrClientAppTests.
// The real EchoServer tests live in the `EchoTcpServerTests` project. Keep a harmless placeholder
// test here to avoid analysis errors from tools that scan this folder.
using NUnit.Framework;

namespace NetSdrClientAppTests
{
    [TestFixture]
    public class EchoServerPlaceholderTests
    {
        [Test]
        public void Placeholder_DoesNotFail()
        {
            Assert.Pass("Placeholder test to satisfy analysis; actual EchoServer tests are in EchoTcpServerTests.");
        }
    }
}
