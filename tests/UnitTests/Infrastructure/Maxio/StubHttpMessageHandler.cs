using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Answers HTTP calls from a scripted queue and records what was sent, so the Maxio client can be
/// tested against the exact request shape the OpenAPI specification calls for.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = new();

    public StubHttpMessageHandler Respond(HttpStatusCode statusCode, string body = "", string contentType = "application/json")
    {
        _responses.Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType)
        });

        return this;
    }

    public StubHttpMessageHandler RespondWith(Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        _responses.Enqueue(factory);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter,
            body));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No scripted response for {request.Method} {request.RequestUri}.");
        }

        return _responses.Dequeue()(request);
    }

    public record RecordedRequest(HttpMethod Method, Uri Uri, string? AuthScheme, string? AuthParameter, string? Body);
}
