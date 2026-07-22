using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// A stand-in Maxio server. Responses are the payload shapes published in the Maxio OpenAPI
/// specification, so the real <c>MaxioBillingClient</c> is exercised end-to-end — request
/// construction, JSON handling, mapping and error translation — without leaving the test process.
/// </summary>
public class MaxioApiStub : HttpMessageHandler
{
    private readonly List<Route> _routes = new();

    /// <summary>Every request the client made, in order, with its body captured.</summary>
    public List<RecordedRequest> Requests { get; } = new();

    public MaxioApiStub Respond(HttpMethod method, string pathAndQuery, HttpStatusCode status, string body)
    {
        _routes.Add(new Route(method, pathAndQuery, _ => new StubResponse(status, body)));
        return this;
    }

    public MaxioApiStub RespondOk(HttpMethod method, string pathAndQuery, string body) =>
        Respond(method, pathAndQuery, HttpStatusCode.OK, body);

    /// <summary>Registers a route whose response depends on how many times it has been called.</summary>
    public MaxioApiStub RespondInSequence(HttpMethod method, string pathAndQuery, params (HttpStatusCode Status, string Body)[] responses)
    {
        var callCount = 0;
        _routes.Add(new Route(method, pathAndQuery, _ =>
        {
            var index = Math.Min(callCount++, responses.Length - 1);
            return new StubResponse(responses[index].Status, responses[index].Body);
        }));
        return this;
    }

    /// <summary>Registers a route that fails at the transport level, as an unreachable host would.</summary>
    public MaxioApiStub Unreachable(HttpMethod method, string pathAndQuery)
    {
        _routes.Add(new Route(method, pathAndQuery, _ => throw new HttpRequestException("No such host is known.")));
        return this;
    }

    public int CallCount(HttpMethod method, string pathAndQuery) =>
        Requests.Count(r => r.Method == method && r.PathAndQuery == pathAndQuery);

    public RecordedRequest? LastRequest(HttpMethod method, string pathAndQuery) =>
        Requests.LastOrDefault(r => r.Method == method && r.PathAndQuery == pathAndQuery);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var pathAndQuery = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery).TrimStart('/');
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(request.Method, pathAndQuery, body,
            request.Headers.Authorization?.Scheme, request.Headers.Authorization?.Parameter));

        var route = _routes.FirstOrDefault(r => r.Method == request.Method && r.PathAndQuery == pathAndQuery);
        if (route is null)
        {
            // Mirrors Maxio's behaviour for an unknown resource rather than silently succeeding.
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"errors\":[\"Not Found\"]}", Encoding.UTF8, "application/json")
            };
        }

        var stub = route.Handler(request);
        return new HttpResponseMessage(stub.Status)
        {
            Content = new StringContent(stub.Body, Encoding.UTF8, "application/json")
        };
    }

    private sealed record Route(HttpMethod Method, string PathAndQuery, Func<HttpRequestMessage, StubResponse> Handler);

    private sealed record StubResponse(HttpStatusCode Status, string Body);
}

public record RecordedRequest(HttpMethod Method, string PathAndQuery, string? Body, string? AuthScheme, string? AuthParameter);
