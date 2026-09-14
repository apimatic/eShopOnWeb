using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

/// <summary>
/// A stand-in for the Maxio Billing API. Routes on "METHOD path" and records every call, so tests
/// can assert not just what came back but how many writes the integration actually issued.
/// </summary>
public class FakeMaxioHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();

    public List<string> Calls { get; } = new();

    public List<string> RequestBodies { get; } = new();

    public int CountOf(string route) => Calls.Count(call => call == route);

    public FakeMaxioHandler Map(string route, HttpStatusCode statusCode, string json)
    {
        _routes[route] = _ => Respond(statusCode, json);
        return this;
    }

    public FakeMaxioHandler Map(string route, Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _routes[route] = responder;
        return this;
    }

    public static HttpResponseMessage Respond(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var route = $"{request.Method} {request.RequestUri!.AbsolutePath}";
        Calls.Add(route);

        RequestBodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));

        if (_routes.TryGetValue(route, out var responder))
        {
            return responder(request);
        }

        return Respond(HttpStatusCode.NotFound, """{"errors":["route not mapped in the test double"]}""");
    }
}
