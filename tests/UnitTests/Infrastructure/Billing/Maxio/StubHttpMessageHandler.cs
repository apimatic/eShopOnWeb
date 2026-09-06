using System.Net;
using System.Net.Http;
using System.Text;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// Answers HTTP calls from a queue of canned responses and records what was asked for.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    public List<string?> RequestBodies { get; } = new();

    public StubHttpMessageHandler Respond(HttpStatusCode statusCode, string? json = null)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = json is null
                ? new StringContent(string.Empty)
                : new StringContent(json, Encoding.UTF8, "application/json")
        });

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No canned response left for {request.RequestUri}.");
        }

        return _responses.Dequeue()(request);
    }
}
