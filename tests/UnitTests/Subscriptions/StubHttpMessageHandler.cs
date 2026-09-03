using System.Net;

namespace Microsoft.eShopWeb.UnitTests.Subscriptions;

/// <summary>
/// Routes each outbound request to a canned (status, json) response by inspecting the request, and
/// records every request/body. This is the SDK's test seam: the MaxioClient takes an HttpClient, so a
/// handler here replaces the network. Bodies are buffered during SendAsync because the SDK disposes
/// request content per attempt (reading it after the call returns throws ObjectDisposedException).
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string?, (HttpStatusCode Status, string Json)> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, string?, (HttpStatusCode, string)> responder)
        => _responder = responder;

    public int CountByMethodAndPath(HttpMethod method, string pathContains) =>
        Requests.Count(r => r.Method == method
            && (r.RequestUri?.AbsolutePath.Contains(pathContains, StringComparison.OrdinalIgnoreCase) ?? false));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult();
        Requests.Add(request);
        Bodies.Add(body);

        var (status, json) = _responder(request, body);
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
        return Task.FromResult(response);
    }
}
