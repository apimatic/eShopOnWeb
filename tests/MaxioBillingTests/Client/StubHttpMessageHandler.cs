using System.Net;

namespace Microsoft.eShopWeb.MaxioBillingTests.Client;

/// <summary>
/// Records every request it is handed and replies from a queue of scripted responses.
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
            Content = new StringContent(json ?? string.Empty, System.Text.Encoding.UTF8, "application/json")
        });

        return this;
    }

    public StubHttpMessageHandler RespondWith(Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        _responses.Enqueue(factory);
        return this;
    }

    public StubHttpMessageHandler Throw<TException>() where TException : Exception, new()
    {
        _responses.Enqueue(_ => throw new TException());
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No scripted response left for {request.Method} {request.RequestUri}.");
        }

        return _responses.Dequeue()(request);
    }
}
