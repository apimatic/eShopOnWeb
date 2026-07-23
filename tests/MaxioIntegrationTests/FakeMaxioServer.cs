using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// A recorded Maxio endpoint: the payload it answers with and the status it answers under.
/// </summary>
public record FakeResponse(HttpStatusCode StatusCode, string Body)
{
    public static FakeResponse Ok(string body) => new(HttpStatusCode.OK, body);
    public static FakeResponse Created(string body) => new(HttpStatusCode.Created, body);
    public static FakeResponse NotFound() => new(HttpStatusCode.NotFound, "{}");
    public static FakeResponse Unprocessable(string body) => new(HttpStatusCode.UnprocessableEntity, body);
}

/// <summary>
/// What the client actually sent, so tests can assert on the request as well as the response —
/// the wire contract matters as much as the mapping.
/// </summary>
public record RecordedRequest(HttpMethod Method, string PathAndQuery, string? Body);

/// <summary>
/// Stands in for the Maxio API. Responses are the shapes documented in the OpenAPI specification
/// (maxio-spec/), so the client is exercised against the contract it is written to rather than
/// against a mock of itself. Every request is recorded and any unmapped route fails loudly rather
/// than silently returning something plausible.
/// </summary>
public class FakeMaxioServer : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<FakeResponse>> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RecordedRequest> _requests = new();

    /// <summary>Set to throw a transport failure instead of answering, to exercise the unreachable path.</summary>
    public Exception? TransportFailure { get; set; }

    public IReadOnlyList<RecordedRequest> Requests => _requests;

    public int CountRequests(HttpMethod method, string pathAndQuery) =>
        _requests.Count(r => r.Method == method &&
            string.Equals(r.PathAndQuery, pathAndQuery, StringComparison.OrdinalIgnoreCase));

    public RecordedRequest? LastRequest(HttpMethod method, string pathAndQuery) =>
        _requests.LastOrDefault(r => r.Method == method &&
            string.Equals(r.PathAndQuery, pathAndQuery, StringComparison.OrdinalIgnoreCase));

    /// <summary>Maps a route. Mapping the same route twice queues the responses, so a retry or a
    /// re-read can be given a different answer from the first call.</summary>
    public FakeMaxioServer Map(HttpMethod method, string pathAndQuery, FakeResponse response)
    {
        var key = Key(method, pathAndQuery);
        if (!_routes.TryGetValue(key, out var queue))
        {
            queue = new Queue<FakeResponse>();
            _routes[key] = queue;
        }

        queue.Enqueue(response);

        return this;
    }

    public FakeMaxioServer MapGet(string pathAndQuery, FakeResponse response) =>
        Map(HttpMethod.Get, pathAndQuery, response);

    public FakeMaxioServer MapPost(string pathAndQuery, FakeResponse response) =>
        Map(HttpMethod.Post, pathAndQuery, response);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (TransportFailure is not null)
        {
            throw TransportFailure;
        }

        var pathAndQuery = request.RequestUri!.PathAndQuery.TrimStart('/');
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        _requests.Add(new RecordedRequest(request.Method, pathAndQuery, body));

        var key = Key(request.Method, pathAndQuery);
        if (!_routes.TryGetValue(key, out var queue) || queue.Count == 0)
        {
            throw new InvalidOperationException(
                $"The client called an unmapped Maxio route: {request.Method} /{pathAndQuery}. " +
                $"Mapped routes: {string.Join(", ", _routes.Keys)}");
        }

        // The last mapped response for a route keeps answering, so repeated reads need not be
        // mapped repeatedly unless the test wants them to differ.
        var response = queue.Count == 1 ? queue.Peek() : queue.Dequeue();

        return new HttpResponseMessage(response.StatusCode)
        {
            Content = new StringContent(response.Body, Encoding.UTF8, "application/json")
        };
    }

    private static string Key(HttpMethod method, string pathAndQuery) => $"{method} {pathAndQuery.TrimStart('/')}";
}
