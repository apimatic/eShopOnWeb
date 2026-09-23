using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.SubscriptionBilling;

/// <summary>
/// Test seam for the Maxio SDK: an <see cref="HttpMessageHandler"/> that answers from a caller-supplied
/// responder and records every request (retries append), per the dotnet-testing guidance.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public int CountOf(HttpMethod method, string pathContains) =>
        Requests.FindAll(r => r.Method == method && (r.RequestUri?.AbsolutePath.Contains(pathContains) ?? false)).Count;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var response = _responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
