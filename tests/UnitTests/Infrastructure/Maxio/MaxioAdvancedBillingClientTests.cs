using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioAdvancedBillingClientTests
{
    private static MaxioAdvancedBillingClient CreateClient(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.chargify.com/")
        };
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new MaxioAdvancedBillingClient(httpClient, settings, NullLogger<MaxioAdvancedBillingClient>.Instance);
    }

    [Fact]
    public async Task ListProductsForProductFamilyAsync_UsesHandlePrefixedPath()
    {
        var handler = new FakeHttpMessageHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"product_family":{"handle":"eshop-subscribe","name":"eShop"}}}]""",
                    Encoding.UTF8,
                    "application/json")
            }
        };

        var plans = await CreateClient(handler).ListProductsForProductFamilyAsync("eshop-subscribe");

        Assert.Equal("/product_families/handle:eshop-subscribe/products.json", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("page=1&per_page=200", handler.LastRequest.RequestUri.Query.TrimStart('?'));
        Assert.Equal("Basic", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal(29900, plans[0].PriceInCents);
    }

    [Fact]
    public async Task ReadCustomerByReferenceAsync_ReturnsNullOn404()
    {
        var handler = new FakeHttpMessageHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("Not Found")
            }
        };

        var customer = await CreateClient(handler).ReadCustomerByReferenceAsync("eshop:buyer-1");

        Assert.Null(customer);
        Assert.Equal("/customers/lookup.json", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("reference=eshop%3Abuyer-1", handler.LastRequest.RequestUri.Query);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_PostsProductHandleAndCustomerId()
    {
        var handler = new FakeHttpMessageHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """{"subscription":{"id":55,"state":"active","product_price_in_cents":2900,"next_assessment_at":"2026-09-19T12:00:00-04:00","product":{"handle":"basic-plan","name":"Basic Plan"}}}""",
                    Encoding.UTF8,
                    "application/json")
            }
        };

        var subscription = await CreateClient(handler).CreateSubscriptionAsync("basic-plan", 42, "eshop:buyer-1:basic-plan");

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/subscriptions.json", handler.LastRequest.RequestUri!.AbsolutePath);
        var body = handler.LastRequestBody;
        Assert.Contains("\"product_handle\":\"basic-plan\"", body);
        Assert.Contains("\"customer_id\":42", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
        Assert.Equal(55, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("basic-plan", subscription.ProductHandle);
        Assert.NotNull(subscription.NextBillingDate);
    }
}

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);
    public HttpRequestMessage? LastRequest { get; private set; }
    public string LastRequestBody { get; private set; } = string.Empty;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return Response;
    }
}
