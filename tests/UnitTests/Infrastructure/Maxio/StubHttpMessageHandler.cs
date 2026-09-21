using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Test seam for the Maxio SDK: an <see cref="HttpMessageHandler"/> that answers from a caller
/// supplied responder and records every request (retries append) plus each request body captured
/// while it is still readable (the SDK disposes request content per attempt).
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content?.ReadAsStringAsync(cancellationToken).Result);
        var response = _responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };
}
