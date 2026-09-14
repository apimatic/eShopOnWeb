using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Services.MaxioSubscriptionServiceTests;

/// <summary>
/// Test seam for the Maxio SDK client (see dotnet-testing skill): returns one canned response per
/// call, in order, so a multi-step flow (e.g. lookup-then-create) can be scripted deterministically
/// without a real HTTP connection.
/// </summary>
public sealed class QueueHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses;

    public List<HttpRequestMessage> Requests { get; } = new();

    public QueueHttpMessageHandler(IEnumerable<(HttpStatusCode Status, string Body)> responses)
    {
        _responses = new Queue<(HttpStatusCode, string)>(responses);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var (status, body) = _responses.Dequeue();
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
