using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The test seam for the Maxio SDK: the client is constructed from an <see cref="HttpClient"/>, so
/// stubbing the handler is what lets these tests drive the real client and the real deserialisation
/// against controlled provider responses.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    /// <summary>Every request the client sent, in order, so outgoing shape can be asserted.</summary>
    public List<HttpRequestMessage> Requests { get; } = new();

    /// <summary>The bodies of the requests the client sent, captured before disposal.</summary>
    public List<string> RequestBodies { get; } = new();

    public HttpRequestMessage LastRequest => Requests[^1];

    public string LastRequestBody => RequestBodies[^1];

    /// <summary>Queues a JSON response. Responses are served in the order they are queued.</summary>
    public StubHttpMessageHandler RespondWith(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"The client sent an unexpected request to {request.RequestUri} with no stubbed response queued.");
        }

        return _responses.Dequeue();
    }
}
