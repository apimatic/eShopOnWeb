using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Test seam for the Maxio SDK: an <see cref="HttpMessageHandler"/> that answers from a supplied
/// responder and records every request (retries append, so this is what you count). Request bodies
/// are buffered during <c>SendAsync</c> because the SDK disposes the content per attempt.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder) =>
        _responder = responder;

    public int CountRequests(HttpMethod method, string pathContains) =>
        Requests.FindAll(r => r.Method == method && (r.RequestUri?.AbsolutePath.Contains(pathContains) ?? false)).Count;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        string? body = request.Content is null ? null : request.Content.ReadAsStringAsync().Result;
        Bodies.Add(body);
        var response = _responder(request, body);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}
