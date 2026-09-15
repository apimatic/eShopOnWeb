using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingTests;

public class MaxioAdvancedBillingClientTests
{
    [Fact]
    public async Task ListProductsForFamily_UsesHandlePrefixedFamilyPath()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var client = new MaxioAdvancedBillingClient(new HttpClient(handler) { BaseAddress = new System.Uri("https://cp-exp-4.chargify.com/") });
        var products = await client.ListProductsForFamilyAsync("eshop-subscribe");

        Assert.NotNull(captured);
        Assert.Contains("/product_families/handle:eshop-subscribe/products.json", captured!.RequestUri!.ToString());
        var product = Assert.Single(products);
        Assert.Equal("eshop-pro", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
    }

    [Fact]
    public async Task FindCustomerByReference_ReturnsNullOn404()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler) { BaseAddress = new System.Uri("https://cp-exp-4.chargify.com/") });

        var customer = await client.FindCustomerByReferenceAsync("eshop:user-1");

        Assert.Null(customer);
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
