using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.TrendEndpoints;

/// <summary>
/// Stands in for stream.wikimedia.org: no network access in tests.
/// </summary>
public sealed class StubWikimediaHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public StubWikimediaHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) =>
        _responder = responder;

    public List<HttpRequestMessage> Requests { get; } = new();

    public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
            Requests.Add(request);
        var response = await _responder(request, cancellationToken);
        response.RequestMessage = request;
        return response;
    }

    public static StubWikimediaHandler Streaming(ScriptedSseStream body) =>
        new((_, _) => Task.FromResult(EventStream(body)));

    public static HttpResponseMessage EventStream(ScriptedSseStream body)
    {
        var content = new StreamContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
