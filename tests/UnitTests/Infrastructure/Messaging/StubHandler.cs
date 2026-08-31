using System.Net;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Messaging;

/// <summary>
/// HttpMessageHandler stub: the SDK client's test seam. Captures every request (retries
/// append) and answers with the configured responder. Bodies are captured inside SendAsync
/// because the SDK disposes request content once the call completes.
/// </summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> RequestBodies { get; } = new();
    public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];
    public string LastRequestBody => RequestBodies.Count == 0 ? string.Empty : RequestBodies[^1];

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public static StubHandler ReturningJson(HttpStatusCode status, string json) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));
        return _responder(request);
    }
}
