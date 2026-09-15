using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioAdvancedBillingClientTests
{
    [Fact]
    public async Task ListProductsForFamilyAsync_DeserializesMaxioProductPayload()
    {
        const string json = """
            [
              {
                "product": {
                  "id": 7126957,
                  "name": "Pro Plan",
                  "handle": "eshop-pro",
                  "description": "Monthly pro",
                  "price_in_cents": 29900,
                  "interval": 1,
                  "interval_unit": "month",
                  "archived_at": null
                }
              }
            ]
            """;

        var handler = new StubHandler((request) =>
        {
            Assert.Contains("product_families/handle:eshop-subscribe/products.json", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var client = CreateClient(handler);
        var products = await client.ListProductsForFamilyAsync("eshop-subscribe", CancellationToken.None);

        var product = Assert.Single(products);
        Assert.Equal("eshop-pro", product.Handle);
        Assert.Equal("Pro Plan", product.Name);
        Assert.Equal(29900, product.PriceInCents);
        Assert.Equal("month", product.IntervalUnit);
    }

    [Fact]
    public async Task FindCustomerByReferenceAsync_ReturnsNullOn404()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var customer = await client.FindCustomerByReferenceAsync("eshoponweb:demouser@microsoft.com", CancellationToken.None);

        Assert.Null(customer);
    }

    private static MaxioAdvancedBillingClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://cp-exp-4.chargify.com/") };
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "cp-exp-4",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new MaxioAdvancedBillingClient(httpClient, options, Substitute.For<ILogger<MaxioAdvancedBillingClient>>());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
