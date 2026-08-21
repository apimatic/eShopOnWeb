using System.Net;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public sealed class MaxioWriteOnceHandlerTests
{
    [Fact]
    public async Task BlocksASecondPostInsideTheSameWriteScope()
    {
        var guard = new MaxioWriteGuard();
        var countingHandler = new CountingHandler();
        var handler = new MaxioWriteOnceHandler(guard) { InnerHandler = countingHandler };
        using var client = new HttpClient(handler);

        using (guard.Begin())
        {
            var response = await client.PostAsync("https://example.test/subscriptions", new StringContent("{}"));
            response.EnsureSuccessStatusCode();

            await Assert.ThrowsAsync<MaxioWriteRetryBlockedException>(
                () => client.PostAsync("https://example.test/subscriptions", new StringContent("{}")));
        }

        Assert.Equal(1, countingHandler.SendCount);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
