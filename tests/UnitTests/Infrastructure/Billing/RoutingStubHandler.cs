using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

/// <summary>
/// Test seam for the Maxio SDK: a fake <see cref="HttpMessageHandler"/> that dispatches on
/// (HTTP method, request path) to a canned response, and records every request (with its serialized body
/// captured while still readable — the SDK disposes request content per attempt). This is the documented
/// way to test APIMatic SDK code without touching SDK internals or the network.
/// </summary>
public sealed class RoutingStubHandler : HttpMessageHandler
{
    private readonly Func<string, string, string?, HttpResponseMessage> _responder;

    public List<(string Method, string Path, string? Body)> Requests { get; } = new();

    public RoutingStubHandler(Func<string, string, string?, HttpResponseMessage> responder)
        => _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.Method.Method;
        var path = request.RequestUri!.AbsolutePath;
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync().ConfigureAwait(false);

        Requests.Add((method, path, body));

        var response = _responder(method, path, body);
        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    public int CountOf(string method, string pathSuffix) =>
        Requests.FindAll(r => r.Method == method && r.Path.EndsWith(pathSuffix, StringComparison.Ordinal)).Count;
}
