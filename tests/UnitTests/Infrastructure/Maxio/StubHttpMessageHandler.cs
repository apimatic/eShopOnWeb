using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>Records the requests a client makes and replays canned responses in order.</summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    public List<string?> RequestBodies { get; } = new();

    public StubHttpMessageHandler Enqueue(HttpStatusCode statusCode, string? json = null) =>
        EnqueueResponse(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json ?? string.Empty, System.Text.Encoding.UTF8, "application/json")
        });

    public StubHttpMessageHandler EnqueueResponse(HttpResponseMessage response)
    {
        _responses.Enqueue(response);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No canned response left for {request.Method} {request.RequestUri}.");
        }

        return _responses.Dequeue();
    }
}
