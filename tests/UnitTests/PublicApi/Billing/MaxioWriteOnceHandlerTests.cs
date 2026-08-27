using System.Net;
using Microsoft.eShopWeb.PublicApi.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Billing;

public class MaxioWriteOnceHandlerTests
{
    [Fact]
    public async Task BlocksASecondPostInsideTheSameLogicalWriteScope()
    {
        var coordinator = new MaxioWriteOnceCoordinator();
        var inner = new CountingHandler();
        var client = new HttpClient(new MaxioWriteOnceHandler(coordinator) { InnerHandler = inner });

        using (coordinator.Begin())
        {
            using var first = await client.PostAsync("https://example.test/subscriptions", new StringContent("{}"));
            await Assert.ThrowsAnyAsync<Exception>(() =>
                client.PostAsync("https://example.test/subscriptions", new StringContent("{}")));
        }

        Assert.Equal(1, inner.Count);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Count { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
