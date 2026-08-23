using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

internal sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Body);

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpRequestMessage, HttpResponseMessage>> _responses;

    public StubHttpMessageHandler(IEnumerable<Func<HttpRequestMessage, HttpResponseMessage>> responses)
    {
        _responses = new ConcurrentQueue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);
    }

    public ConcurrentQueue<CapturedRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue(new CapturedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            body));

        if (!_responses.TryDequeue(out var responder))
        {
            throw new InvalidOperationException($"No stub response remains for {request.Method} {request.RequestUri}.");
        }

        return responder(request);
    }
}
