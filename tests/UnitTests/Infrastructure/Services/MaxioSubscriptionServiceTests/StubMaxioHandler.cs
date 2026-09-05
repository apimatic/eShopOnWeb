using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services.MaxioSubscriptionServiceTests;

/// <summary>
/// Returns queued responses in call order. The SDK client owns no mocking helpers of its own;
/// the HttpClient passed into MaxioAdvancedBillingClient is the seam (see plugin skill dotnet-testing).
/// </summary>
public sealed class StubMaxioHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Json)> _responses;

    public List<HttpRequestMessage> Requests { get; } = new();

    public StubMaxioHandler(IEnumerable<(HttpStatusCode Status, string Json)> responses)
    {
        _responses = new Queue<(HttpStatusCode, string)>(responses);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var (status, json) = _responses.Count > 0
            ? _responses.Dequeue()
            : (HttpStatusCode.InternalServerError, "{}");

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }
}
