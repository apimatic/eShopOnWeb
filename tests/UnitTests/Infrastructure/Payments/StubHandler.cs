using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Payments;

/// <summary>Answers SDK requests from a queue of responders and records every request it saw.</summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responders = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    // Buffered during SendAsync: the SDK disposes request content before the test can read it.
    public List<string?> Bodies { get; } = new();

    public StubHandler Then(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responders.Enqueue(responder);
        return this;
    }

    public StubHandler ThenJson(HttpStatusCode status, string json) =>
        Then((_, _) => Task.FromResult(Json(status, json)));

    public StubHandler ThenThrow(Exception exception) =>
        Then((_, _) => Task.FromException<HttpResponseMessage>(exception));

    /// <summary>Never answers; completes only when the request is cancelled.</summary>
    public StubHandler ThenHang() =>
        Then(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });

    public string? HeaderOf(int requestIndex, string name) =>
        Requests[requestIndex].Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
        if (_responders.Count == 0)
            throw new InvalidOperationException($"Unexpected request #{Requests.Count}: {request.Method} {request.RequestUri}");

        var response = await _responders.Dequeue()(request, cancellationToken);
        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
