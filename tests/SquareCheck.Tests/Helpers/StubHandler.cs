using System.Net;
using System.Text;

namespace SquareCheck.Tests.Helpers;

public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string?> Bodies { get; } = [];
    public string? LastBody => Bodies.Count == 0 ? null : Bodies[^1];
    public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _responder = responder;

    public static StubHandler Returning(HttpStatusCode status, string json)
        => new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

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
}
