using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioWriteGuardHandlerTests
{
    [TestMethod]
    public async Task BlocksASecondPostWithinOneLogicalWrite()
    {
        var terminal = new CountingHandler();
        var guard = new MaxioWriteGuardHandler { InnerHandler = terminal };
        using var client = new HttpClient(guard);
        using var scope = MaxioWriteGuardHandler.BeginScope();

        using var first = await client.PostAsync("https://example.test/customers", new StringContent("{}"));
        await Assert.ThrowsExceptionAsync<MaxioWriteRetryBlockedException>(
            () => client.PostAsync("https://example.test/customers", new StringContent("{}")));

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(1, terminal.SendCount);
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
