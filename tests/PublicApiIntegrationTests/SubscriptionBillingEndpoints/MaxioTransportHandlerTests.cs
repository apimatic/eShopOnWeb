using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionBilling;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionBillingEndpoints;

[TestClass]
public class MaxioTransportHandlerTests
{
    [TestMethod]
    public async Task SingleSendScopeBlocksASecondPostBeforeItReachesTheNetwork()
    {
        var context = new MaxioTransportContext();
        var terminal = new CountingHandler();
        var guard = new MaxioTransportHandler(context) { InnerHandler = terminal };
        using var client = new HttpClient(guard);
        using var operation = context.BeginOperation(singleSend: true);

        var first = await client.PostAsync("https://example.test/subscriptions", new StringContent("{}"));
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        await Assert.ThrowsExceptionAsync<MaxioDuplicateSendBlockedException>(() =>
            client.PostAsync("https://example.test/subscriptions", new StringContent("{}")));
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
