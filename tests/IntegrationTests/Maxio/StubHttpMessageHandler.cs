using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

/// <summary>
/// The SDK's test seam is the <see cref="HttpClient"/> it is constructed with. This handler answers
/// requests from a caller-supplied responder and records every request (retries append), so a test
/// can assert what the integration actually sent — e.g. that no create was POSTed on an idempotent hit.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<(HttpMethod Method, string Path)> Requests { get; } = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public int CountOf(HttpMethod method, string pathSuffix)
        => Requests.FindAll(r => r.Method == method && r.Path.EndsWith(pathSuffix, StringComparison.Ordinal)).Count;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
        var response = _responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
