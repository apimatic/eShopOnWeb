using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioWriteGuardHandlerTest
{
    [TestMethod]
    public async Task BlocksASecondPostInsideTheSameProviderWriteScope()
    {
        var callContext = new MaxioCallContext();
        var innerHandler = new CountingHandler();
        using var guard = new MaxioWriteGuardHandler(callContext) { InnerHandler = innerHandler };
        using var client = new HttpClient(guard);
        using var scope = callContext.Begin(atMostOneWrite: true);

        using var first = await client.PostAsync("https://example.test/subscriptions.json", new StringContent("{}"));
        await Assert.ThrowsExceptionAsync<MaxioWriteRetryBlockedException>(
            () => client.PostAsync("https://example.test/subscriptions.json", new StringContent("{}")));

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(1, innerHandler.SendCount);
    }

    [TestMethod]
    public async Task AllowsMultipleReadsInsideAWriteScope()
    {
        var callContext = new MaxioCallContext();
        var innerHandler = new CountingHandler();
        using var guard = new MaxioWriteGuardHandler(callContext) { InnerHandler = innerHandler };
        using var client = new HttpClient(guard);
        using var scope = callContext.Begin(atMostOneWrite: true);

        using var first = await client.GetAsync("https://example.test/subscriptions/lookup.json");
        using var second = await client.GetAsync("https://example.test/subscriptions/lookup.json");

        Assert.AreEqual(2, innerHandler.SendCount);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }
}
