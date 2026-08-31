using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Twilio;

/// <summary>
/// The SDK test seam: an HttpMessageHandler fake, so no real network calls happen.
/// </summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();

    // The SDK disposes request content after sending, so bodies are captured at send time.
    public List<string?> RequestBodies { get; } = new();
    public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];
    public string? LastRequestBody => RequestBodies.Count == 0 ? null : RequestBodies[^1];

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : this((request, _) => responder(request)) { }

    public StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
        return _responder(request, Requests.Count - 1);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
}
