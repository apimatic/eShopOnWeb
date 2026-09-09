using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio.TestSupport;

/// <summary>
/// Routes Maxio-shaped JSON responses by method and path so the SDK client can be
/// exercised without network access. Records every request for assertions.
/// </summary>
public sealed class MaxioStubHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();
    private readonly Dictionary<string, HttpResponseMessage> _shared = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    /// <summary>
    /// The request bodies, captured at send time (the SDK disposes request content afterwards).
    /// Index-aligned with <see cref="Requests"/>.
    /// </summary>
    public List<string?> Bodies { get; } = new();

    public void On(string methodAndPath, Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _routes[methodAndPath] = responder;

    public void On(string methodAndPath, HttpStatusCode status, string json) =>
        _routes[methodAndPath] = _ => JsonResponse(status, json);

    public void OnShared(string methodAndPath, HttpStatusCode status, string json) =>
        _shared[methodAndPath] = JsonResponse(status, json);

    public int Count(string pathSubstring) =>
        Requests.Count(r => r.RequestUri!.PathAndQuery.Contains(pathSubstring, StringComparison.OrdinalIgnoreCase));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null
            ? null
            : request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        var key = $"{request.Method} {request.RequestUri!.AbsolutePath}";

        if (_routes.TryGetValue(key, out var responder))
        {
            return Task.FromResult(responder(request));
        }

        if (_shared.TryGetValue(key, out var shared))
        {
            return Task.FromResult(Clone(shared));
        }

        return Task.FromResult(JsonResponse(HttpStatusCode.NotFound, "{\"errors\":[\"no stub for " + key + "\"]}"));
    }

    private static HttpResponseMessage Clone(HttpResponseMessage response)
    {
        var clone = new HttpResponseMessage(response.StatusCode)
        {
            Content = new StringContent(response.Content.ReadAsStringAsync().GetAwaiter().GetResult(), Encoding.UTF8, "application/json")
        };
        return clone;
    }

    public static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}
