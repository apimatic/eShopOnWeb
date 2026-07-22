using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// The test seam for the Maxio SDK: the <see cref="HttpClient"/> handed to
/// <c>MaxioBillingClient</c> is the only injection point, so a handler stubbed here decides exactly
/// what the provider "returns" and records exactly what was sent.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly List<Route> _routes = new();
    private readonly List<RecordedRequest> _requests = new();

    /// <summary>Every request the client made, in order.</summary>
    public IReadOnlyList<RecordedRequest> Requests => _requests;

    public RecordedRequest LastRequest => _requests.Count > 0
        ? _requests[^1]
        : throw new InvalidOperationException("No request was made.");

    /// <summary>
    /// Requests whose URL contains the given fragment, compared against the decoded URL so
    /// fragments can be written as intended (<c>handle:api-call</c>) rather than percent-encoded.
    /// </summary>
    public IReadOnlyList<RecordedRequest> RequestsFor(string pathFragment) =>
        _requests.Where(request => request.DecodedUri.Contains(pathFragment, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Responds to a matching request with a JSON body and status.</summary>
    public StubHttpMessageHandler Respond(HttpMethod method, string pathFragment, HttpStatusCode status, string json)
    {
        _routes.Add(new Route(method, pathFragment, status, json, null));
        return this;
    }

    /// <summary>Responds 200 with a JSON body.</summary>
    public StubHttpMessageHandler RespondOk(HttpMethod method, string pathFragment, string json) =>
        Respond(method, pathFragment, HttpStatusCode.OK, json);

    /// <summary>
    /// Responds to successive matching calls with successive replies, repeating the last one once
    /// the sequence is exhausted. Used to prove pagination is followed, and to simulate provider
    /// state changing between calls.
    /// </summary>
    public StubHttpMessageHandler RespondInSequence(HttpMethod method,
        string pathFragment,
        params (HttpStatusCode Status, string Json)[] replies)
    {
        var remaining = new Queue<(HttpStatusCode Status, string Json)>(replies);
        _routes.Add(new Route(method, pathFragment, HttpStatusCode.OK, null,
            () => remaining.Count > 1 ? remaining.Dequeue() : remaining.Peek()));
        return this;
    }

    /// <summary>Successive 200 replies, for sequences where only the body changes.</summary>
    public StubHttpMessageHandler RespondInSequence(HttpMethod method, string pathFragment, params string[] jsonBodies) =>
        RespondInSequence(method, pathFragment, jsonBodies.Select(json => (HttpStatusCode.OK, json)).ToArray());

    /// <summary>Makes a matching request fail at the transport level, as an unreachable host would.</summary>
    public StubHttpMessageHandler Unreachable(HttpMethod method, string pathFragment)
    {
        _routes.Add(new Route(method, pathFragment, HttpStatusCode.OK, null, null) { Throws = true });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        _requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body)
        {
            AuthorizationScheme = request.Headers.Authorization?.Scheme,
            AuthorizationParameter = request.Headers.Authorization?.Parameter
        });

        var route = _routes.FirstOrDefault(candidate => candidate.Matches(request));
        if (route is null)
        {
            // An unstubbed call is a test bug, and must not masquerade as a provider 404.
            throw new InvalidOperationException(
                $"No stub configured for {request.Method} {request.RequestUri}. Stubbed routes: " +
                string.Join(", ", _routes.Select(r => $"{r.Method} *{r.PathFragment}*")));
        }

        if (route.Throws)
        {
            throw new HttpRequestException("Simulated network failure.");
        }

        var (status, json) = route.Next();
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class Route
    {
        public Route(HttpMethod method,
            string pathFragment,
            HttpStatusCode status,
            string? json,
            Func<(HttpStatusCode Status, string Json)>? sequence)
        {
            Method = method;
            PathFragment = pathFragment;
            Status = status;
            Json = json;
            Sequence = sequence;
        }

        public HttpMethod Method { get; }
        public string PathFragment { get; }
        public HttpStatusCode Status { get; }
        public string? Json { get; }
        public Func<(HttpStatusCode Status, string Json)>? Sequence { get; }
        public bool Throws { get; init; }

        public bool Matches(HttpRequestMessage request) =>
            request.Method == Method
            && Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)
                .Contains(PathFragment, StringComparison.OrdinalIgnoreCase);

        public (HttpStatusCode Status, string Json) Next() => Sequence?.Invoke() ?? (Status, Json ?? "{}");
    }
}

/// <summary>A request the client actually sent, captured for assertion.</summary>
public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body)
{
    public string Path => Uri.AbsolutePath;

    public string Query => Uri.Query;

    /// <summary>The full absolute URI, so tests can prove which server was targeted.</summary>
    public string AbsoluteUri => Uri.AbsoluteUri;

    /// <summary>
    /// The absolute URI with percent-encoding undone, so assertions can be written against the
    /// value that was intended (for example <c>handle:eshop-subscribe</c>) rather than its wire
    /// encoding (<c>handle%3Aeshop-subscribe</c>).
    /// </summary>
    public string DecodedUri => Uri.UnescapeDataString(Uri.AbsoluteUri);

    /// <summary>The scheme of the outgoing Authorization header, e.g. <c>Basic</c>.</summary>
    public string? AuthorizationScheme { get; init; }

    /// <summary>The raw credential portion of the outgoing Authorization header.</summary>
    public string? AuthorizationParameter { get; init; }
}
