using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

/// <summary>
/// Test seam for the Maxio SDK: an <see cref="HttpMessageHandler"/> that answers from a supplied
/// responder and records every request (and its body, buffered before the SDK disposes it).
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : request.Content.ReadAsStringAsync().Result);
        var response = _responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}
