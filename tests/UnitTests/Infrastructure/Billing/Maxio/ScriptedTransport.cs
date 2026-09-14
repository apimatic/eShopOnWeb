using System.Collections.Concurrent;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers from a script keyed on method + path, and
/// records every call so tests can assert on what the integration did and did not send.
/// </summary>
internal sealed class ScriptedTransport : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Func<HttpRequestMessage, HttpResponseMessage>>> _script = new();
    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _fallbacks = new();
    private readonly ConcurrentQueue<RecordedCall> _calls = new();

    public IReadOnlyCollection<RecordedCall> Calls => _calls.ToArray();

    public int CountOf(HttpMethod method, string path) =>
        _calls.Count(c => c.Method == method.Method && c.Path == path);

    public RecordedCall? LastCall(HttpMethod method, string path) =>
        _calls.LastOrDefault(c => c.Method == method.Method && c.Path == path);

    /// <summary>Answers every GET of <paramref name="path"/> with the same response.</summary>
    public ScriptedTransport OnGet(string path, HttpResponseMessage response) =>
        Always(HttpMethod.Get, path, _ => Clone(response));

    /// <summary>Answers every POST to <paramref name="path"/> with the same response.</summary>
    public ScriptedTransport OnPost(string path, HttpResponseMessage response) =>
        Always(HttpMethod.Post, path, _ => Clone(response));

    public ScriptedTransport OnPost(string path, Func<HttpRequestMessage, HttpResponseMessage> factory) =>
        Always(HttpMethod.Post, path, factory);

    /// <summary>Queues one-shot responses, consumed in order before the standing answer is used.</summary>
    public ScriptedTransport EnqueueGet(string path, params HttpResponseMessage[] responses) =>
        Enqueue(HttpMethod.Get, path, responses);

    public ScriptedTransport EnqueuePost(string path, params HttpResponseMessage[] responses) =>
        Enqueue(HttpMethod.Post, path, responses);

    private ScriptedTransport Always(HttpMethod method, string path, Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        _fallbacks[Key(method.Method, path)] = factory;
        return this;
    }

    private ScriptedTransport Enqueue(HttpMethod method, string path, HttpResponseMessage[] responses)
    {
        var queue = _script.GetOrAdd(Key(method.Method, path), _ => new ConcurrentQueue<Func<HttpRequestMessage, HttpResponseMessage>>());
        foreach (var response in responses)
        {
            queue.Enqueue(_ => Clone(response));
        }

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        _calls.Enqueue(new RecordedCall(request.Method.Method, path, request.RequestUri.Query, body));

        var key = Key(request.Method.Method, path);

        if (_script.TryGetValue(key, out var queue) && queue.TryDequeue(out var scripted))
        {
            return scripted(request);
        }

        if (_fallbacks.TryGetValue(key, out var fallback))
        {
            return fallback(request);
        }

        throw new InvalidOperationException($"No scripted response for {request.Method} {path}{request.RequestUri.Query}.");
    }

    private static string Key(string method, string path) => $"{method} {path}";

    private static HttpResponseMessage Clone(HttpResponseMessage source) => new(source.StatusCode)
    {
        // Each attempt needs its own content stream, since a retried call reads the body twice.
        Content = new StringContent(
            source.Content.ReadAsStringAsync().GetAwaiter().GetResult(),
            System.Text.Encoding.UTF8,
            "application/json"),
    };

    internal sealed record RecordedCall(string Method, string Path, string Query, string? Body);
}
