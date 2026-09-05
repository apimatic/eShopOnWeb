using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services.Maxio;

/// <summary>
/// A test double for HttpClient's handler that plays back a fixed sequence of responses,
/// one per request, and records the requests it saw so tests can assert on them.
/// MaxioBillingService's calls to Maxio happen in a deterministic order for a given
/// scenario, so scripting the exact sequence keeps these tests simple and explicit.
/// </summary>
internal class SequenceHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    public List<HttpRequestMessage> Requests { get; } = new();

    public SequenceHttpMessageHandler Then(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        _responses.Enqueue(responseFactory);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        }

        return Task.FromResult(_responses.Dequeue()(request));
    }
}
