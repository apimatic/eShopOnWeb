using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure;

/// <summary>
/// A scripted stand-in for the billing provider's HTTP surface. The provider SDK is constructed from
/// an <see cref="HttpClient"/> we supply, so this handler is the seam that lets the integration's real
/// behaviour — request shapes, envelope handling, magnitude conversion, error translation — be
/// exercised without any live traffic.
/// </summary>
public sealed class StubBillingServer : HttpMessageHandler
{
    private readonly List<Route> _routes = new();

    /// <summary>Every request the integration actually sent, in order.</summary>
    public List<RecordedRequest> Requests { get; } = new();

    public StubBillingServer Get(string pathContains, string json, HttpStatusCode status = HttpStatusCode.OK)
        => Enqueue(HttpMethod.Get, pathContains, json, status);

    public StubBillingServer Post(string pathContains, string json, HttpStatusCode status = HttpStatusCode.OK)
        => Enqueue(HttpMethod.Post, pathContains, json, status);

    public StubBillingServer Put(string pathContains, string json, HttpStatusCode status = HttpStatusCode.OK)
        => Enqueue(HttpMethod.Put, pathContains, json, status);

    public StubBillingServer Delete(string pathContains, string json, HttpStatusCode status = HttpStatusCode.OK)
        => Enqueue(HttpMethod.Delete, pathContains, json, status);

    /// <summary>Requests recorded for a path fragment, for asserting what the integration sent.</summary>
    public IReadOnlyList<RecordedRequest> RequestsFor(string pathContains)
        => Requests.Where(r => r.Path.Contains(pathContains, StringComparison.OrdinalIgnoreCase)).ToList();

    private StubBillingServer Enqueue(HttpMethod method, string pathContains, string json, HttpStatusCode status)
    {
        var existing = _routes.FirstOrDefault(r => r.Method == method && r.PathContains == pathContains);
        if (existing is null)
        {
            existing = new Route(method, pathContains);
            _routes.Add(existing);
        }

        existing.Responses.Enqueue(new ScriptedResponse(status, json));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var uri = request.RequestUri ?? throw new InvalidOperationException("The SDK issued a request with no URI.");

        Requests.Add(new RecordedRequest(
            request.Method,
            uri,
            uri.AbsolutePath + uri.Query,
            body,
            request.Headers.Authorization is null
                ? null
                : $"{request.Headers.Authorization.Scheme} {request.Headers.Authorization.Parameter}"));

        // Longest path fragment first, so "/subscriptions/1/components/2" beats "/subscriptions".
        var route = _routes
            .Where(r => r.Method == request.Method && (uri.AbsolutePath + uri.Query).Contains(r.PathContains, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.PathContains.Length)
            .FirstOrDefault();

        if (route is null)
        {
            throw new InvalidOperationException(
                $"The integration sent an unscripted {request.Method} {uri.AbsolutePath}{uri.Query}. " +
                "Script it on the stub, or fix the call.");
        }

        // The last scripted response for a route repeats, so a test only scripts a sequence when it
        // actually cares about one.
        var scripted = route.Responses.Count > 1 ? route.Responses.Dequeue() : route.Responses.Peek();

        return new HttpResponseMessage(scripted.Status)
        {
            Content = new StringContent(scripted.Json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class Route
    {
        public Route(HttpMethod method, string pathContains)
        {
            Method = method;
            PathContains = pathContains;
        }

        public HttpMethod Method { get; }
        public string PathContains { get; }
        public Queue<ScriptedResponse> Responses { get; } = new();
    }

    private sealed record ScriptedResponse(HttpStatusCode Status, string Json);
}

public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Path, string Body, string? Authorization);

/// <summary>A handler that always fails at the transport level, to exercise the unavailable path.</summary>
public sealed class UnreachableBillingServer : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => throw new HttpRequestException("No such host is known.");
}
