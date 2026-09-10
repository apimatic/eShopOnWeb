using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

/// <summary>
/// A deterministic, offline <see cref="HttpMessageHandler"/> for exercising
/// <c>MaxioBillingService</c> without touching the network. The responder decides
/// the reply per request; every request is recorded for assertions.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public ConcurrentQueue<(string Method, string PathAndQuery)> Requests { get; } = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public int CountOf(string method, string pathContains)
    {
        var n = 0;
        foreach (var (m, p) in Requests)
        {
            if (string.Equals(m, method, StringComparison.OrdinalIgnoreCase) && p.Contains(pathContains, StringComparison.OrdinalIgnoreCase))
            {
                n++;
            }
        }
        return n;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue((request.Method.Method, request.RequestUri!.PathAndQuery));
        return Task.FromResult(_responder(request));
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };
}
