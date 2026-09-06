using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// Replays canned Maxio responses and records the requests that produced them, so client behaviour
/// can be asserted without a live billing site. Requests are snapshotted rather than kept, because
/// the client disposes them as soon as the call returns.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    internal sealed record CapturedRequest(HttpMethod Method, Uri Uri, string Body);

    private readonly Queue<(HttpStatusCode StatusCode, string Body)> _responses = new();

    public List<CapturedRequest> Requests { get; } = new();

    public StubHttpMessageHandler Enqueue(HttpStatusCode statusCode, string body = "")
    {
        _responses.Enqueue((statusCode, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No canned response left for {request.Method} {request.RequestUri}.");
        }

        var (statusCode, responseBody) = _responses.Dequeue();

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json"),
            RequestMessage = request
        };
    }
}
