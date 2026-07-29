using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Test double for <see cref="HttpClient"/> that routes requests to canned responses
/// keyed by "<c>METHOD path</c>" and records every request it received, so tests can
/// assert both the responses consumed and the calls made.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<string> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
        return Task.FromResult(_responder(request));
    }

    public int CountOf(HttpMethod method, string absolutePath)
        => Requests.FindAll(r => r == $"{method} {absolutePath}").Count;

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
        => new(statusCode) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}
