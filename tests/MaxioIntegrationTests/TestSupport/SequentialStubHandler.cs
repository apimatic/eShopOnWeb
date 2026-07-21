using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.TestSupport;

/// <summary>
/// The SDK's test seam: an <see cref="HttpMessageHandler"/> that returns a pre-queued response
/// for each outgoing request, in order, and records every request so tests can assert on the
/// method/path/query/body actually sent to Maxio. No real network call is ever made.
/// </summary>
public sealed class SequentialStubHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;

    public SequentialStubHandler(params HttpResponseMessage[] responses)
    {
        _responses = new Queue<HttpResponseMessage>(responses);
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    /// <summary>
    /// The request body text captured at send time, one entry per <see cref="Requests"/> entry
    /// (null for requests with no body). The SDK disposes each request's content once it has
    /// been sent, so reading <c>Requests[i].Content</c> after the call returns throws
    /// <see cref="ObjectDisposedException"/> — read the body here instead.
    /// </summary>
    public List<string?> RequestBodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"SequentialStubHandler received an unexpected {request.Method} {request.RequestUri} with no queued response left.");
        }

        return _responses.Dequeue();
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    public static HttpResponseMessage Empty(HttpStatusCode status) => new(status);
}
