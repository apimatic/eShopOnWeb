using System.Net;
using System.Net.Http;
using System.Text;

namespace Microsoft.eShopWeb.UnitTests.Billing.Maxio;

/// <summary>
/// Stands in for Maxio. Answers are queued per "METHOD /path" so a test can script a sequence of
/// replies for the same route, and every request is recorded for assertions.
/// </summary>
public class StubMaxioHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<StubResponse>> _scripted = new(StringComparer.OrdinalIgnoreCase);

    public List<RecordedRequest> Requests { get; } = new();

    public StubMaxioHandler Respond(HttpMethod method, string path, HttpStatusCode statusCode, string? json = null)
    {
        var key = Key(method.Method, path);
        if (!_scripted.TryGetValue(key, out var queue))
        {
            queue = new Queue<StubResponse>();
            _scripted[key] = queue;
        }

        queue.Enqueue(new StubResponse(statusCode, json));
        return this;
    }

    public int CountOf(HttpMethod method, string path) =>
        Requests.Count(r => string.Equals(r.Method, method.Method, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));

    public RecordedRequest? LastOf(HttpMethod method, string path) =>
        Requests.LastOrDefault(r => string.Equals(r.Method, method.Method, StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(request.Method.Method, path, request.RequestUri.Query, body,
            request.Headers.Authorization?.ToString()));

        var key = Key(request.Method.Method, path);
        if (!_scripted.TryGetValue(key, out var queue) || queue.Count == 0)
        {
            throw new InvalidOperationException($"No stubbed Maxio response for {key}.");
        }

        // The last scripted answer for a route repeats, so tests only script what they care about.
        var scripted = queue.Count == 1 ? queue.Peek() : queue.Dequeue();

        var response = new HttpResponseMessage(scripted.StatusCode)
        {
            Content = new StringContent(scripted.Json ?? string.Empty, Encoding.UTF8, "application/json")
        };
        response.Headers.TryAddWithoutValidation("X-Request-Id", Guid.NewGuid().ToString());
        return response;
    }

    private static string Key(string method, string path) => $"{method} {path}";

    private readonly record struct StubResponse(HttpStatusCode StatusCode, string? Json);

    public record RecordedRequest(string Method, string Path, string Query, string? Body, string? Authorization);
}
