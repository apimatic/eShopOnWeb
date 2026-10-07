using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.DigitalFiles;

/// <summary>
/// A loopback HTTP server that announces <c>announcedLength</c> bytes, sends <c>sentBytes</c>, then goes
/// silent while keeping the connection open — a Box download that stalls on a real socket.
/// </summary>
public sealed class StallingHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serve;

    public StallingHttpServer(int announcedLength, int sentBytes)
    {
        _listener = new TcpListener(IPAddress.Loopback, ListenPort());
        _listener.Start();
        _serve = ServeAsync(announcedLength, sentBytes);
    }

    // Stay inside the machine's assigned port block when one is configured; otherwise take an ephemeral port.
    private static int ListenPort() =>
        int.TryParse(Environment.GetEnvironmentVariable("APP_PORT_BLOCK_BASE"), out var basePort) &&
        int.TryParse(Environment.GetEnvironmentVariable("APP_PORT_BLOCK_SIZE"), out var size) && size > 0
            ? basePort + size - 1
            : 0;

    public Uri Url => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/file");

    /// <summary>Opens the response the way the Box client does: returns after the headers.</summary>
    public async Task<HttpResponseMessage> OpenAsync(HttpClient client) =>
        await client.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead);

    private async Task ServeAsync(int announcedLength, int sentBytes)
    {
        try
        {
            using var socket = await _listener.AcceptSocketAsync(_stop.Token);
            var buffer = new byte[4096];
            await socket.ReceiveAsync(buffer, SocketFlags.None, _stop.Token);
            var head = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/pdf\r\nContent-Length: {announcedLength}\r\n\r\n");
            await socket.SendAsync(head, SocketFlags.None, _stop.Token);
            await socket.SendAsync(new byte[sentBytes], SocketFlags.None, _stop.Token);
            await Task.Delay(Timeout.Infinite, _stop.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            await _serve;
        }
        catch (Exception)
        {
        }
        _stop.Dispose();
    }
}
