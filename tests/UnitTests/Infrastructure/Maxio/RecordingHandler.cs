using System.Net;
using System.Net.Http;
using System.Text;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Records every request it receives and replies with the next queued response, so tests can assert
/// both the outgoing request sequence (paths/bodies) and drive MaxioSubscriptionService through
/// canned Maxio API responses without any real network call.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders;

    public List<(HttpMethod Method, string PathAndQuery, string? Body)> Requests { get; } = new();

    public RecordingHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responders)
    {
        _responders = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responders);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.PathAndQuery, body));

        if (_responders.Count == 0)
        {
            throw new InvalidOperationException($"No more responses queued for {request.Method} {request.RequestUri}");
        }

        return _responders.Dequeue()(request);
    }

    public static Func<HttpRequestMessage, HttpResponseMessage> RespondWith(HttpStatusCode statusCode, string? json = null) =>
        _ => new HttpResponseMessage(statusCode)
        {
            Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json")
        };
}
