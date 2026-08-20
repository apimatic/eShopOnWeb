using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioBillingGatewayTest
{
    [TestMethod]
    public async Task ListsOnlySupportedActiveProductsAndUsesStableFamilyHandle()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                [
                  {"product":{"name":"Pro","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","product_family":{"handle":"eshop-subscribe"}}},
                  {"product":{"name":"Basic","handle":"basic-plan","price_in_cents":2900,"interval":1,"interval_unit":"month","product_family":{"handle":"eshop-subscribe"}}},
                  {"product":{"name":"Hidden","handle":"other-plan","price_in_cents":100,"interval":1,"interval_unit":"month","product_family":{"handle":"eshop-subscribe"}}}
                ]
                """)
        });
        var sdkOptions = new MaxioAdvancedBillingClientOptions();
        sdkOptions.Server.Production.Us.BaseUrl = "https://maxio.test";
        var sdkClient = new MaxioAdvancedBillingClient(new HttpClient(handler), sdkOptions);
        var gateway = new MaxioBillingGateway(
            sdkClient,
            Options.Create(new MaxioOptions
            {
                ApiKey = "test-only-not-a-credential",
                Subdomain = "test-only",
                ProductFamilyHandle = "eshop-subscribe",
                BaseUrl = "https://maxio.test"
            }),
            new MaxioResponseContext(),
            new MaxioWriteGuard());

        var plans = await gateway.ListPlansAsync(CancellationToken.None);

        Assert.AreEqual(2, plans.Count);
        Assert.AreEqual("basic-plan", plans[0].ProductHandle);
        Assert.AreEqual("eshop-pro", plans[1].ProductHandle);
        Assert.AreEqual(HttpMethod.Get, handler.Requests[0].Method);
        var requestUri = handler.Requests[0].RequestUri;
        Assert.IsNotNull(requestUri);
        StringAssert.Contains(
            requestUri.AbsolutePath,
            "/product_families/handle%3Aeshop-subscribe/products.json");
        StringAssert.Contains(requestUri.Query, "include_archived=false");
        StringAssert.Contains(requestUri.Query, "per_page=100");
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }
}
