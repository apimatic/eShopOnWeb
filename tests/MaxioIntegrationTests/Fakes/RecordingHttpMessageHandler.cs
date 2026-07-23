using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Stands in for Maxio. Responses are queued per (method, path) so a test states exactly what the
/// provider returns, and every outbound request is recorded so a test can assert what the client
/// actually sent — verb, path and body.
/// </summary>
public sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<Func<HttpRequestMessage, HttpResponseMessage>>> _responses = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RecordedRequest> _requests = new();

    /// <summary>Every request the client issued, in order.</summary>
    public IReadOnlyList<RecordedRequest> Requests => _requests;

    /// <summary>Queues a JSON response for the next request matching this method and path.</summary>
    public RecordingHttpMessageHandler RespondJson(HttpMethod method, string path, string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        => Respond(method, path, _ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    /// <summary>Queues a bodiless status response, e.g. a 404 for a lookup that finds nothing.</summary>
    public RecordingHttpMessageHandler RespondStatus(HttpMethod method, string path, HttpStatusCode statusCode)
        => Respond(method, path, _ => new HttpResponseMessage(statusCode) { Content = new StringContent(string.Empty) });

    /// <summary>Queues a transport-level failure, as if the provider were unreachable.</summary>
    public RecordingHttpMessageHandler RespondThrows(HttpMethod method, string path, Exception exception)
        => Respond(method, path, _ => throw exception);

    public RecordingHttpMessageHandler Respond(HttpMethod method, string path, Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        var key = Key(method, path);
        if (!_responses.TryGetValue(key, out var queue))
        {
            queue = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>();
            _responses[key] = queue;
        }

        queue.Enqueue(factory);
        return this;
    }

    /// <summary>The requests issued against one method and path.</summary>
    public IReadOnlyList<RecordedRequest> RequestsFor(HttpMethod method, string path) =>
        _requests.Where(r => r.Method == method && string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)).ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        _requests.Add(new RecordedRequest(request.Method, path, request.RequestUri.Query, body,
            request.Headers.Authorization?.ToString(), request.RequestUri));

        var key = Key(request.Method, path);
        if (!_responses.TryGetValue(key, out var queue) || queue.Count == 0)
        {
            throw new InvalidOperationException(
                $"No response was queued for {request.Method} {path}. Queued keys: {string.Join(", ", _responses.Keys)}");
        }

        return queue.Dequeue()(request);
    }

    private static string Key(HttpMethod method, string path) => $"{method} {path}";
}

/// <summary>One outbound request the client issued.</summary>
public sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string? Body, string? Authorization, Uri Uri);
