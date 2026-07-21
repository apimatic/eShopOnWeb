using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// The SDK's only test seam is the <see cref="HttpClient"/> constructor argument (per the SDK's
/// own testing guidance) — there is no mocking helper shipped with it. This handler intercepts
/// every outgoing request, hands it to a caller-supplied responder, and records every request it
/// saw so tests can assert on the exact outgoing call (method, path, body).
/// </summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<RecordedRequest> Requests { get; } = new();

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body));
        return _responder(request);
    }

    public record RecordedRequest(HttpMethod Method, Uri Uri, string? Body);

    /// <summary>A responder that returns the given canned responses in order, one per request, for tests where a single MaxioBillingClient call issues a known sequence of HTTP requests.</summary>
    public static Func<HttpRequestMessage, HttpResponseMessage> Sequence(params HttpResponseMessage[] responses)
    {
        var queue = new Queue<HttpResponseMessage>(responses);
        return _ => queue.Count > 0
            ? queue.Dequeue()
            : throw new InvalidOperationException("Test error: more HTTP requests were made than canned responses were queued.");
    }
}
