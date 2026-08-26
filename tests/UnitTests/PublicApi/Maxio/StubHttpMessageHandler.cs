using System.Net;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Maxio;

/// <summary>
/// Routes HTTP requests to canned responses and records every request for assertions.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly List<Func<HttpRequestMessage, HttpResponseMessage?>> _routes = new();

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> RequestBodies { get; } = new();

    public void Route(HttpMethod method, string pathContains, HttpResponseMessage response)
    {
        _routes.Add(request =>
            request.Method == method && request.RequestUri!.PathAndQuery.Contains(pathContains)
                ? response
                : null);
    }

    public void Route(HttpMethod method, string pathContains, Func<HttpResponseMessage> responseFactory)
    {
        _routes.Add(request =>
            request.Method == method && request.RequestUri!.PathAndQuery.Contains(pathContains)
                ? responseFactory()
                : null);
    }

    public bool Requested(HttpMethod method, string pathContains) =>
        Requests.Any(r => r.Method == method && r.RequestUri!.PathAndQuery.Contains(pathContains));

    public string BodyOf(HttpMethod method, string pathContains)
    {
        for (var i = 0; i < Requests.Count; i++)
        {
            if (Requests[i].Method == method && Requests[i].RequestUri!.PathAndQuery.Contains(pathContains))
            {
                return RequestBodies[i];
            }
        }

        throw new InvalidOperationException($"No request recorded for {method} *{pathContains}*");
    }

    public string LastBodyOf(HttpMethod method, string pathContains)
    {
        for (var i = Requests.Count - 1; i >= 0; i--)
        {
            if (Requests[i].Method == method && Requests[i].RequestUri!.PathAndQuery.Contains(pathContains))
            {
                return RequestBodies[i];
            }
        }

        throw new InvalidOperationException($"No request recorded for {method} *{pathContains}*");
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));

        foreach (var route in _routes)
        {
            var response = route(request);
            if (response is not null)
            {
                return response;
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"No stub registered for {request.Method} {request.RequestUri}")
        };
    }

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
