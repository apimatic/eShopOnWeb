using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

/// <summary>
/// Test seam for the Maxio SDK: an <see cref="HttpMessageHandler"/> that answers each request from a
/// caller-supplied responder and records every request (retries append) for assertions.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, (HttpStatusCode Status, string Json)> _responder;

    public List<(HttpMethod Method, string Path)> Requests { get; } = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> responder) => _responder = responder;

    public int CountOf(HttpMethod method, string pathSuffix) =>
        Requests.FindAll(r => r.Method == method && r.Path.EndsWith(pathSuffix, StringComparison.Ordinal)).Count;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.Method, request.RequestUri!.AbsolutePath));

        var (status, json) = _responder(request);
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
        return Task.FromResult(response);
    }
}
