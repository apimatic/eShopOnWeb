using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Replays queued HTTP responses and records what was sent, so tests can assert on the exact wire
/// requests the Maxio client produces.
/// </summary>
internal class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = new();

    public void Enqueue(HttpStatusCode statusCode, string body) =>
        _responses.Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

    /// <summary>Queues a response built from the request, for exercising retry behaviour.</summary>
    public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> factory) => _responses.Enqueue(factory);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri,
            request.Headers.Authorization,
            request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No stub response queued for {request.Method} {request.RequestUri}.");
        }

        return _responses.Dequeue()(request);
    }

    internal record RecordedRequest(HttpMethod Method, Uri? Uri, AuthenticationHeaderValue? Authorization, string? Body);
}
