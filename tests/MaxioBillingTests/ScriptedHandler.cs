using System.Net;

namespace Microsoft.eShopWeb.MaxioBillingTests;

/// <summary>
/// Test seam for the Maxio SDK: an <see cref="HttpMessageHandler"/> whose responder is driven by the
/// request's method and path. Records every request (retries append) and buffers each body while it is
/// still readable (the SDK disposes request content per attempt).
/// </summary>
public sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();

    public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public int Count(HttpMethod method, string pathSuffix) =>
        Requests.Count(r => r.Method == method && (r.RequestUri?.AbsolutePath.EndsWith(pathSuffix) ?? false));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var response = _responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
