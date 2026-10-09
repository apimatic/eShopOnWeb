using System.Net;
using System.Net.Sockets;
using Microsoft.eShopWeb.SquareCheck.SignIn;
using Xunit;

namespace Microsoft.eShopWeb.SquareCheck.UnitTests;

public sealed class OAuthCallbackListenerTests
{
    private const string State = "listener-test-state-0123456789";

    [Fact]
    public async Task LocalhostRedirect_IsReachableOnEveryLoopbackAddressTheBrowserMayUse()
    {
        var port = FreePort();
        await using var listener = await OAuthCallbackListener.StartAsync(new Uri($"http://localhost:{port}/callback"), State, default);
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false });

        var hosts = new List<string> { "127.0.0.1" };
        if (Socket.OSSupportsIPv6)
        {
            hosts.Add("[::1]");
        }

        foreach (var host in hosts)
        {
            using var response = await http.GetAsync($"http://{host}:{port}/callback?code=x&state=not-this-run");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.False(listener.WaitForCallbackAsync(default).IsCompleted);
    }

    [Fact]
    public async Task ApprovedVisit_IsHeldUntilTheToolKnowsTheBusiness()
    {
        await using var listener = await OAuthCallbackListener.StartAsync(new Uri("http://127.0.0.1:0/callback"), State, default);
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false });

        var visit = http.GetAsync($"{listener.CallbackUri}?code=abc&state={State}");
        var callback = await listener.WaitForCallbackAsync(default).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("abc", callback.Code);
        await Task.Delay(100);
        Assert.False(visit.IsCompleted);

        listener.Complete(BrowserPage.Connected("Corner Café"));
        using var response = await visit;
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Corner Café", WebUtility.HtmlDecode(body));
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
