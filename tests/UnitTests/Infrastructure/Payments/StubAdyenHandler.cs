using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Payments;

/// <summary>
/// Stands in for Adyen at the HttpClient seam. Request bodies are buffered while still readable,
/// because the SDK disposes request content once the attempt completes.
/// </summary>
public sealed class StubAdyenHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string?, int, CancellationToken, Task<HttpResponseMessage>> _responder;

    public StubAdyenHandler(Func<HttpRequestMessage, string?, int, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    public static StubAdyenHandler Returning(HttpStatusCode status, string json) =>
        new((_, _, _, _) => Task.FromResult(Json(status, json)));

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Bodies.Add(body);
        var response = await _responder(request, body, Requests.Count, cancellationToken);
        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
