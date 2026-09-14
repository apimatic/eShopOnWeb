using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Routes requests by "METHOD path" (path includes the query string) to a canned response,
/// and records every request it saw for assertions.
/// </summary>
public class FakeMaxioHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();
    public List<HttpRequestMessage> Requests { get; } = new();

    public FakeMaxioHandler On(HttpMethod method, string pathAndQuery, HttpResponseMessage response)
    {
        _routes[Key(method, pathAndQuery)] = _ => response;
        return this;
    }

    public FakeMaxioHandler On(HttpMethod method, string pathAndQuery, Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _routes[Key(method, pathAndQuery)] = responder;
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var key = Key(request.Method, request.RequestUri!.PathAndQuery.TrimStart('/'));
        if (!_routes.TryGetValue(key, out var responder))
        {
            throw new InvalidOperationException($"No fake route configured for {key}");
        }

        return Task.FromResult(responder(request));
    }

    private static string Key(HttpMethod method, string pathAndQuery) => $"{method} {pathAndQuery}";

    public static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };
}
