using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// A stand-in Maxio HTTP endpoint. The SDK client's <see cref="HttpClient"/> is the only seam it
/// offers, so tests drive the integration by scripting the responses this handler returns and
/// then asserting on the requests it captured.
/// </summary>
/// <remarks>
/// Routes are matched in registration order by a predicate over the outgoing request, so a test
/// asserts against tokens that must appear in the path (an id, a handle, an endpoint segment)
/// rather than against a guessed full URL. Anything unmatched answers 404, which is exactly how
/// the real API reports an unknown entity.
/// </remarks>
public sealed class MaxioApiStub : HttpMessageHandler
{
    private readonly List<Route> _routes = new();

    /// <summary>Every request the integration sent, in order, with its body already read.</summary>
    public List<CapturedRequest> Requests { get; } = new();

    public MaxioApiStub Respond(
        HttpMethod method,
        Func<Uri, bool> pathMatches,
        HttpStatusCode statusCode,
        string body,
        string contentType = "application/json")
    {
        _routes.Add(new Route(method, pathMatches, _ => new StubResponse(statusCode, body, contentType)));
        return this;
    }

    /// <summary>Registers a route whose response depends on how many times it has already been hit.</summary>
    public MaxioApiStub RespondInSequence(
        HttpMethod method,
        Func<Uri, bool> pathMatches,
        params (HttpStatusCode StatusCode, string Body)[] responses)
    {
        var hits = 0;
        _routes.Add(new Route(method, pathMatches, _ =>
        {
            var index = Math.Min(hits, responses.Length - 1);
            hits++;
            var (statusCode, body) = responses[index];
            return new StubResponse(statusCode, body, "application/json");
        }));

        return this;
    }

    /// <summary>Registers a route that fails at the transport layer rather than answering.</summary>
    public MaxioApiStub Throw(HttpMethod method, Func<Uri, bool> pathMatches, Exception exception)
    {
        _routes.Add(new Route(method, pathMatches, _ => throw exception));
        return this;
    }

    /// <summary>Convenience matcher: the request path contains every one of <paramref name="tokens"/>.</summary>
    public static Func<Uri, bool> PathContaining(params string[] tokens) =>
        uri => tokens.All(t => uri.AbsolutePath.Contains(t, StringComparison.Ordinal));

    /// <summary>Convenience matcher: the request path ends with <paramref name="suffix"/>.</summary>
    public static Func<Uri, bool> PathEndingWith(string suffix) =>
        uri => uri.AbsolutePath.EndsWith(suffix, StringComparison.Ordinal);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body, request.Headers.Authorization?.Parameter));

        var route = _routes.FirstOrDefault(r => r.Method == request.Method && r.PathMatches(request.RequestUri!));
        if (route is null)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    """{"errors":["No stubbed route matched this request."]}""",
                    Encoding.UTF8,
                    "application/json"),
                RequestMessage = request
            };
        }

        var stubbed = route.Respond(request);

        return new HttpResponseMessage(stubbed.StatusCode)
        {
            Content = new StringContent(stubbed.Body, Encoding.UTF8, stubbed.ContentType),
            RequestMessage = request
        };
    }

    private sealed record Route(HttpMethod Method, Func<Uri, bool> PathMatches, Func<HttpRequestMessage, StubResponse> Respond);

    private sealed record StubResponse(HttpStatusCode StatusCode, string Body, string ContentType);
}

/// <summary>One outgoing request, captured for assertion.</summary>
public sealed record CapturedRequest(HttpMethod Method, Uri Uri, string Body, string? AuthorizationParameter);
