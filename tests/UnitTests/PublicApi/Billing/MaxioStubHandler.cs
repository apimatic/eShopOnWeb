using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Billing;

/// <summary>
/// Fake Maxio HTTP layer: routes by "METHOD /path", records every request (with its body, captured while readable).
/// A route responder may throw (e.g. HttpRequestException) to simulate a connection failure.
/// </summary>
public sealed class MaxioStubHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, string?, Task<HttpResponseMessage>>> _routes = new();

    public ConcurrentQueue<(HttpMethod Method, string Path, string Query, string? Body)> Requests { get; } = new();

    public MaxioStubHandler On(HttpMethod method, string path, Func<HttpRequestMessage, string?, Task<HttpResponseMessage>> responder)
    {
        _routes[$"{method.Method} {path}"] = responder;
        return this;
    }

    public MaxioStubHandler OnSync(HttpMethod method, string path, Func<HttpRequestMessage, string?, HttpResponseMessage> responder) =>
        On(method, path, (r, b) => Task.FromResult(responder(r, b)));

    public MaxioStubHandler OnJson(HttpMethod method, string path, HttpStatusCode status, string json) =>
        OnSync(method, path, (_, _) => Json(status, json));

    /// <summary>Full request URIs and the Authorization scheme each request carried.</summary>
    public ConcurrentQueue<(Uri Uri, string? AuthScheme)> Sent { get; } = new();

    public int Count(HttpMethod method, string path) =>
        Requests.Count(r => r.Method == method && r.Path == path);

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        Requests.Enqueue((request.Method, path, request.RequestUri.Query, body));
        Sent.Enqueue((request.RequestUri, request.Headers.Authorization?.Scheme));

        if (!_routes.TryGetValue($"{request.Method.Method} {path}", out var responder))
        {
            return Json(HttpStatusCode.NotFound, "{\"errors\":[\"no stub route\"]}");
        }

        // Honour cancellation even when a responder hangs (simulates a provider that never answers).
        var pending = responder(request, body);
        var finished = await Task.WhenAny(pending, Task.Delay(Timeout.Infinite, cancellationToken));
        if (finished != pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        var response = await pending;
        response.RequestMessage = request;
        return response;
    }
}
