using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioBillingTests;

/// <summary>
/// The test seam for the Maxio SDK: the SDK never owns its <see cref="HttpClient"/>, so replacing the
/// handler is how these tests drive real request/response behaviour without touching the network.
/// Responses are returned in the order they were queued, and every outgoing request is recorded so a test
/// can assert what the integration actually sent.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public List<RecordedRequest> Requests { get; } = new();

    /// <summary>Queues a JSON body with the given status code.</summary>
    public StubHttpMessageHandler Enqueue(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        return this;
    }

    /// <summary>Queues an empty response with the given status code.</summary>
    public StubHttpMessageHandler EnqueueStatus(HttpStatusCode statusCode)
    {
        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        });

        return this;
    }

    /// <summary>Builds an <see cref="HttpClient"/> backed by this handler.</summary>
    public HttpClient CreateClient() => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"The integration made an unexpected {request.Method} request to '{request.RequestUri}'. " +
                $"Requests so far: {string.Join(", ", Requests.Select(r => $"{r.Method} {r.Uri.AbsolutePath}"))}");
        }

        return _responses.Dequeue();
    }

    public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body)
    {
        public string Path => Uri.AbsolutePath;

        public string PathAndQuery => Uri.PathAndQuery;
    }
}
