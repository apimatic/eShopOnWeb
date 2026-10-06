using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.OrderEndpoints;

/// <summary>
/// Stands in for Adyen at the HttpClient seam: no network is ever touched. Each request is answered by
/// the next queued responder (or the fallback), and recorded with its body read while still readable.
/// </summary>
public sealed class StubAdyenHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> _responders = new();

    public List<RecordedRequest> Requests { get; } = new();

    public Func<HttpRequestMessage, Task<HttpResponseMessage>> Fallback { get; set; } =
        _ => Task.FromResult(Json(HttpStatusCode.InternalServerError, """{"status":500,"errorCode":"000","message":"no stub queued","errorType":"internal"}"""));

    public void Enqueue(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) => _responders.Enqueue(responder);

    public void Enqueue(HttpStatusCode status, string json) => Enqueue(_ => Task.FromResult(Json(status, json)));

    public void EnqueueConnectionFailure() =>
        Enqueue(_ => throw new HttpRequestException("connection reset by stub"));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests)
        {
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!,
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase),
                body));
        }

        var responder = _responders.TryDequeue(out var queued) ? queued : Fallback;
        var response = await responder(request);
        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    public sealed record RecordedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? Body)
    {
        public JsonElement BodyJson => JsonDocument.Parse(Body!).RootElement;

        public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
    }
}
