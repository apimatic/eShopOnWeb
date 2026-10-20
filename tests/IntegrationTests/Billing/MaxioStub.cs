#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

/// <summary>
/// Fake Maxio site behind the SDK's HttpClient seam. Responds per "METHOD /path" and records every request
/// (bodies are captured while still readable — the SDK disposes request content after each attempt).
/// </summary>
public class MaxioStub : HttpMessageHandler
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> _routes = new();

    public List<(string Route, string? Body)> Requests { get; } = new();

    public MaxioStub On(string route, Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        _routes[route] = responder;
        return this;
    }

    public MaxioStub On(string route, HttpStatusCode status, string json) =>
        On(route, _ => Task.FromResult(Json(status, json)));

    public int Count(string route)
    {
        lock (_lock)
        {
            return Requests.Count(r => r.Route == route);
        }
    }

    public string? LastBody(string route)
    {
        lock (_lock)
        {
            return Requests.LastOrDefault(r => r.Route == route).Body;
        }
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var route = $"{request.Method} {request.RequestUri!.AbsolutePath}";
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_lock)
        {
            Requests.Add((route, body));
        }

        if (!_routes.TryGetValue(route, out var responder))
        {
            return Json(HttpStatusCode.NotFound, "{}");
        }

        var response = await responder(request);
        response.RequestMessage = request;
        return response;
    }
}
