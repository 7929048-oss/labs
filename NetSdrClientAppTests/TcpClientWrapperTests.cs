using NUnit.Framework;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NetSdrClientApp.Networking;

namespace NetSdrClientAppTests
{
	[TestFixture]
	public class TcpClientWrapperTests
	{
		[Test]
		public void Connect_NoServer_DoesNotThrow_ConnectedFalse()
		{
			var wrapper = new TcpClientWrapper("127.0.0.1", 65000);
			Assert.DoesNotThrow(() => wrapper.Connect());
			Assert.IsFalse(wrapper.Connected);
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

				Assert.IsTrue(wrapper.Connected);

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
				Assert.IsTrue(serverReadTcs.Task.IsCompleted, "Server did not receive message from wrapper");
				CollectionAssert.AreEqual(payload, serverReadTcs.Task.Result);

				var msgTcs = new TaskCompletionSource<byte[]>();
				wrapper.MessageReceived += (s, e) => msgTcs.TrySetResult(e);

				var response = Encoding.UTF8.GetBytes("pong");
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
	}
}

