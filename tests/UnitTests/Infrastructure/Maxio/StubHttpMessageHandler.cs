using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Test seam for the Maxio SDK: a fake <see cref="HttpMessageHandler"/> that answers from a caller-supplied
/// responder and records every request (method + path) so tests can assert what the SDK actually sent —
/// e.g. that a create was NOT called when an existing subscription should have been reused.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public List<(string Method, string Path, string Query)> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.Method.Method, request.RequestUri!.AbsolutePath, request.RequestUri!.Query));
        var response = _responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }

    public int Count(string method, string pathContains) =>
        Requests.FindAll(r => r.Method == method && r.Path.Contains(pathContains)).Count;
}
