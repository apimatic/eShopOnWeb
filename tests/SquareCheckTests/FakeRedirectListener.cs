using SquareCheck;

namespace SquareCheckTests;

// Test double for IRedirectListener that returns pre-configured callbacks and
// records responses without binding to any network port.
internal sealed class FakeRedirectListener : IRedirectListener
{
    private readonly Queue<OAuthCallback> _incoming;
    private readonly List<(int StatusCode, string ContentType, string Body)> _sent = new();

    public IReadOnlyList<(int StatusCode, string ContentType, string Body)> SentResponses => _sent;

    // Pass one OAuthCallback per expected WaitAsync call.
    // An empty queue causes WaitAsync to block until the token is cancelled.
    public FakeRedirectListener(params OAuthCallback[] incoming)
    {
        _incoming = new Queue<OAuthCallback>(incoming);
    }

    public Task<OAuthCallback> WaitAsync(CancellationToken ct)
    {
        if (_incoming.Count > 0)
            return Task.FromResult(_incoming.Dequeue());

        // No pre-configured response: block until cancelled (simulates timeout/Ctrl+C).
        var tcs = new TaskCompletionSource<OAuthCallback>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => tcs.TrySetCanceled(ct));
        if (ct.IsCancellationRequested) tcs.TrySetCanceled(ct);
        return tcs.Task;
    }

    public Task RespondAsync(int statusCode, string contentType, string body, CancellationToken ct)
    {
        _sent.Add((statusCode, contentType, body));
        return Task.CompletedTask;
    }

    public void Dispose() { }
}
