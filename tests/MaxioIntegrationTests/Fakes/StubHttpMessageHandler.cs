using System.Net;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// The test seam for the Maxio SDK: the SDK is constructed over an <see cref="HttpClient"/> we
/// supply, so stubbing the handler is the only way to drive it without live traffic.
/// </summary>
/// <remarks>
/// A request that matches no route throws rather than returning a default response. A silent
/// default would let a test pass while the client called the wrong endpoint entirely.
/// </remarks>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly List<Route> _routes = new();

    /// <summary>Every request the client made, in order, with its body captured as text.</summary>
    internal List<RecordedRequest> Requests { get; } = new();

    /// <summary>Answers matching requests with the same response every time.</summary>
    internal StubHttpMessageHandler Respond(HttpMethod method,
        string pathContains,
        HttpStatusCode status,
        string json)
    {
        _routes.Add(new Route(method, pathContains, new List<(HttpStatusCode, string)> { (status, json) }, repeatLast: true));
        return this;
    }

    /// <summary>
    /// Answers matching requests with each response in turn, so paged reads can be driven page by
    /// page. The last response repeats once the sequence is exhausted.
    /// </summary>
    internal StubHttpMessageHandler RespondInSequence(HttpMethod method,
        string pathContains,
        params (HttpStatusCode Status, string Json)[] responses)
    {
        _routes.Add(new Route(method, pathContains, responses.ToList(), repeatLast: true));
        return this;
    }

    /// <summary>Simulates a network-level failure, which never reaches the SDK as an SDK error.</summary>
    internal StubHttpMessageHandler Fail(HttpMethod method, string pathContains, Exception exception)
    {
        _routes.Add(new Route(method, pathContains, exception));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body, request.Headers.Authorization?.Parameter));

        var route = _routes.FirstOrDefault(r => r.Matches(request));
        if (route is null)
        {
            throw new InvalidOperationException(
                $"No stubbed response for {request.Method} {request.RequestUri}. " +
                "The client called an endpoint the test did not expect.");
        }

        if (route.Exception is not null)
        {
            throw route.Exception;
        }

        var (status, json) = route.Next();

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            RequestMessage = request
        };
    }

    private sealed class Route
    {
        private readonly List<(HttpStatusCode Status, string Json)> _responses;
        private readonly bool _repeatLast;
        private int _index;

        internal Route(HttpMethod method,
            string pathContains,
            List<(HttpStatusCode, string)> responses,
            bool repeatLast)
        {
            Method = method;
            PathContains = pathContains;
            _responses = responses;
            _repeatLast = repeatLast;
        }

        internal Route(HttpMethod method, string pathContains, Exception exception)
        {
            Method = method;
            PathContains = pathContains;
            _responses = new List<(HttpStatusCode, string)>();
            _repeatLast = true;
            Exception = exception;
        }

        private HttpMethod Method { get; }

        private string PathContains { get; }

        internal Exception? Exception { get; }

        internal bool Matches(HttpRequestMessage request) =>
            request.Method == Method &&
            request.RequestUri!.PathAndQuery.Contains(PathContains, StringComparison.OrdinalIgnoreCase);

        internal (HttpStatusCode Status, string Json) Next()
        {
            if (_index < _responses.Count)
            {
                return _responses[_index++];
            }

            return _repeatLast
                ? _responses[^1]
                : throw new InvalidOperationException($"The stub for {Method} {PathContains} ran out of responses.");
        }
    }
}

/// <summary>A request the client actually sent, so tests can assert on the wire, not just the result.</summary>
internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body, string? AuthorizationParameter);
