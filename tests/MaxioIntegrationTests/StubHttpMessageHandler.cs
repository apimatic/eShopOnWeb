using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The test seam for the Maxio SDK: the client is constructed from an <see cref="HttpClient"/>, so
/// substituting its handler is the only way to drive the integration without live traffic.
/// </summary>
/// <remarks>
/// Records every outgoing request so tests can assert what was actually put on the wire — the
/// method, the path, and the JSON body — not merely that a method was called.
/// </remarks>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, StubResponse> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, StubResponse> responder)
        => _responder = responder;

    /// <summary>Every request the SDK sent, in order.</summary>
    public List<RecordedRequest> Requests { get; } = new();

    public RecordedRequest LastRequest =>
        Requests.Count > 0
            ? Requests[^1]
            : throw new InvalidOperationException("No request was sent.");

    /// <summary>Responds to every request with the same JSON body and status.</summary>
    public static StubHttpMessageHandler AlwaysReturns(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(_ => new StubResponse(status, json));

    /// <summary>Responds with each queued response in turn, so multi-call flows can be scripted.</summary>
    public static StubHttpMessageHandler Sequence(params StubResponse[] responses)
    {
        var index = 0;
        return new StubHttpMessageHandler(_ =>
        {
            if (index >= responses.Length)
            {
                throw new InvalidOperationException(
                    $"The integration sent more requests than the {responses.Length} response(s) the test scripted.");
            }

            return responses[index++];
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            body,
            request.Headers.Authorization?.Parameter));

        var stub = _responder(request);

        return new HttpResponseMessage(stub.StatusCode)
        {
            Content = new StringContent(stub.Body, Encoding.UTF8, stub.ContentType),
            RequestMessage = request
        };
    }
}

/// <summary>A canned HTTP response for the stub handler to return.</summary>
public sealed record StubResponse(HttpStatusCode StatusCode, string Body, string ContentType = "application/json")
{
    public static StubResponse Ok(string json) => new(HttpStatusCode.OK, json);
    public static StubResponse Created(string json) => new(HttpStatusCode.Created, json);
    public static StubResponse NotFound(string json = "{\"errors\":[\"Not Found\"]}") => new(HttpStatusCode.NotFound, json);
    public static StubResponse UnprocessableEntity(string json) => new((HttpStatusCode)422, json);
}

/// <summary>What the integration actually sent.</summary>
public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body, string? AuthorizationParameter)
{
    public string Path => Uri.AbsolutePath;
    public string Query => Uri.Query;
}
