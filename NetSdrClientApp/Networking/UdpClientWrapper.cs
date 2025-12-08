﻿using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;

namespace NetSdrClientApp.Networking;

public class UdpClientWrapper : IUdpClient, IDisposable // <-- Implement IDisposable
{
    private readonly IPEndPoint _localEndPoint;
    private CancellationTokenSource? _cts;
    private UdpClient? _udpClient;
    
    public event EventHandler<byte[]>? MessageReceived;

    public UdpClientWrapper(int port)
    {
        _localEndPoint = new IPEndPoint(IPAddress.Any, port);
    }

    public async Task StartListeningAsync()
    {
        _cts?.Dispose(); 
        _cts = new CancellationTokenSource();
            Debug.WriteLine("Start listening for UDP messages...");

        try
        {
            _udpClient = new UdpClient(_localEndPoint);
            while (!_cts.Token.IsCancellationRequested)
            {
                UdpReceiveResult result = await _udpClient.ReceiveAsync(_cts.Token);
                MessageReceived?.Invoke(this, result.Buffer);

                Debug.WriteLine($"Received from {result.RemoteEndPoint}");
            }
        }
        catch (OperationCanceledException)
        {
            //empty
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error receiving message: {ex.Message}");
        }
    }

    public void StopListening()
    {
            try
            {
                _cts?.Cancel();
                _udpClient?.Close();
                Debug.WriteLine("Stopped listening for UDP messages.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error while stopping: {ex.Message}");
            }
    }

    public void Exit()
    {
        StopListening();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;
    
        try
        {
            StopListening();
            _udpClient?.Dispose();
            _cts?.Dispose();
        }
        catch
        {
            // Swallow exceptions during dispose
        }
    }

    public override int GetHashCode()
    {
        var payload = $"{_localEndPoint.Address}|{_localEndPoint.Port}";

        var hashProvider = SHA512.Create();

        using (hashProvider)
        {
            var hash = hashProvider.ComputeHash(Encoding.UTF8.GetBytes(payload));

            return BitConverter.ToInt32(hash, 0);
        }
    }

    public override bool Equals(object? obj)
    {
        if (obj is UdpClientWrapper other)
        {
            return _localEndPoint.Equals(other._localEndPoint);
        }

        return false; 
    }
}