using System.Net;
using Square;
using Square.Core.Configuration;
using Square.Servers;

namespace SquareCheckTests;

// Minimal stub HttpMessageHandler for testing Square SDK calls without a real network.
// The request body is buffered in SendAsync because the SDK disposes request content
// after each attempt, making it unreadable by the time the test inspects LastBody.
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();

    public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];
    public string? LastBody => Bodies.Count == 0 ? null : Bodies[^1];

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null
            ? null
            : request.Content.ReadAsStringAsync().GetAwaiter().GetResult());

        var response = _responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }

    // Build a SquareClient that returns the same status and JSON for every call.
    // Uses Oauth2ClientSecret so no OAuth flow is triggered during tests.
    internal static (SquareClient Client, StubHandler Handler) ClientReturning(
        HttpStatusCode status, string json)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
        var client = new SquareClient(
            new HttpClient(handler),
            new SquareClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2ClientSecret = "test-token",
                Retry = RetryOptions.Disabled(),
            });
        return (client, handler);
    }
}
