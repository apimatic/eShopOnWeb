using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Returns canned responses in call order. Once exhausted it repeats the last response, so a
/// retried call (the SDK retries idempotent GETs on 5xx by default) doesn't throw from an empty
/// queue and instead exercises the real retry-then-fail path.
/// </summary>
internal sealed class QueueStubHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Json)> _responses;
    private (HttpStatusCode Status, string Json) _last;

    public List<HttpRequestMessage> Requests { get; } = new();

    public QueueStubHandler(IEnumerable<(HttpStatusCode Status, string Json)> responses)
    {
        _responses = new Queue<(HttpStatusCode, string)>(responses);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);

        if (_responses.Count > 0)
        {
            _last = _responses.Dequeue();
        }

        var response = new HttpResponseMessage(_last.Status)
        {
            Content = new StringContent(_last.Json, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
