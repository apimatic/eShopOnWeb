using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The <see cref="HttpClient"/> the SDK is constructed from is the only test seam it offers, so provider
/// responses are stubbed here. Every outgoing request is recorded — including its body — so tests can
/// assert what was actually sent, not merely that a method ran.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, StubResponse> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, string, StubResponse> responder)
    {
        _responder = responder;
    }

    /// <summary>Always answers with the same status and body, whatever is requested.</summary>
    public static StubHttpMessageHandler Always(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new((_, _) => new StubResponse(statusCode, json));

    /// <summary>Answers each request in turn from the supplied sequence.</summary>
    public static StubHttpMessageHandler Sequence(params StubResponse[] responses)
    {
        var remaining = new Queue<StubResponse>(responses);
        return new StubHttpMessageHandler((_, _) => remaining.Count > 0
            ? remaining.Dequeue()
            : new StubResponse(HttpStatusCode.NotFound, string.Empty));
    }

    /// <summary>Every request the client made, in order.</summary>
    public List<RecordedRequest> Requests { get; } = new();

    public RecordedRequest LastRequest => Requests[^1];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            body,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter));

        var stub = _responder(request, body);

        return new HttpResponseMessage(stub.StatusCode)
        {
            Content = new StringContent(stub.Body, Encoding.UTF8, "application/json"),
            RequestMessage = request
        };
    }
}

public record StubResponse(HttpStatusCode StatusCode, string Body);

public record RecordedRequest(HttpMethod Method, Uri Uri, string Body, string? AuthScheme, string? AuthParameter);
