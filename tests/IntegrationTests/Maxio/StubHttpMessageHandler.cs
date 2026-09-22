#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

/// <summary>
/// Test seam for the Maxio SDK: an <see cref="HttpMessageHandler"/> that answers from a supplied
/// responder and records every request (retries append), so tests can assert what was actually sent.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    public int CountRequests(HttpMethod method, string absolutePathEndsWith)
    {
        var count = 0;
        foreach (var r in Requests)
        {
            if (r.Method == method &&
                r.RequestUri is not null &&
                r.RequestUri.AbsolutePath.EndsWith(absolutePathEndsWith, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }
        return count;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var response = _responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}
