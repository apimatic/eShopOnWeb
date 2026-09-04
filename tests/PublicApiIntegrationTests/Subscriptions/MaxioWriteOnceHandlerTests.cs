using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Subscriptions;

[TestClass]
public class MaxioWriteOnceHandlerTests
{
    [TestMethod]
    public async Task RefusesASecondPostWithinOneWriteScope()
    {
        var inner = new CountingHandler();
        using var client = new HttpClient(new MaxioWriteOnceHandler { InnerHandler = inner });
        using var scope = MaxioWriteOnceHandler.BeginScope();

        using var first = await client.PostAsync("https://provider.test/customers", new StringContent("{}"));
        await Assert.ThrowsExceptionAsync<MaxioWriteRetryRefusedException>(
            () => client.PostAsync("https://provider.test/customers", new StringContent("{}")));

        Assert.AreEqual(1, inner.RequestCount);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
