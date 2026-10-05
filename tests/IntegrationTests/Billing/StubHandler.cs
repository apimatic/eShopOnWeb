#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

/// <summary>Answers every request from a delegate and records what was sent, so no test reaches the network.</summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : this((request, _) => Task.FromResult(responder(request)))
    {
    }

    public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    /// <summary>Request bodies, buffered inside SendAsync (the SDK disposes request content after each attempt).</summary>
    public List<string?> Bodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
        var response = await _responder(request, cancellationToken);
        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
