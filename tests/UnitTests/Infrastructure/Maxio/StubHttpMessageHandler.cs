using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.MaxioBilling;

/// <summary>
/// Records the requests a client makes and answers them from a queued script, so the wire format the
/// Maxio client produces and consumes can be asserted without touching the network.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = new();

    public StubHttpMessageHandler Respond(HttpStatusCode statusCode, string? json = null)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json ?? string.Empty, System.Text.Encoding.UTF8, "application/json")
        });

        return this;
    }

    public StubHttpMessageHandler Respond(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responses.Enqueue(responder);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body, request.Headers.Authorization?.ToString()));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No scripted response for {request.Method} {request.RequestUri}.");
        }

        return _responses.Dequeue()(request);
    }

    public record RecordedRequest(HttpMethod Method, Uri Uri, string? Body, string? Authorization);
}
