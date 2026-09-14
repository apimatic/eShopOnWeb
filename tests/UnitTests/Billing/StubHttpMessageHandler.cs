using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Billing;

/// <summary>
/// The SDK testing seam: a fake <see cref="HttpMessageHandler"/> that answers requests from a
/// responder, capturing each outgoing request (method, path, body) so tests can assert on the wire.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _responder;

    public List<CapturedRequest> Requests { get; } = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder)
        => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body));
        return Task.FromResult(_responder(request, body));
    }

    public sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Body);
}
