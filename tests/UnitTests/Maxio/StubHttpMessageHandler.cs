using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// Answers Maxio calls from a scripted list of responses and records what was sent, so the
/// integration can be exercised over a real <see cref="HttpClient"/> pipeline without a network.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = new();

    public StubHttpMessageHandler RespondWith(HttpStatusCode statusCode, string json = "")
    {
        _responses.Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        return this;
    }

    public StubHttpMessageHandler RespondWith(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responses.Enqueue(responder);
        return this;
    }

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
                $"Unexpected Maxio call: {request.Method} {request.RequestUri}. No response was scripted.");
        }

        return _responses.Dequeue()(request);
    }

    public record RecordedRequest(HttpMethod Method, Uri Uri, string? Body);
}
