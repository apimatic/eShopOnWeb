using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing.Models;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioAdvancedBillingClientTests
{
    [Fact]
    public async Task ListProductsForProductFamilyAsync_UsesHandlePrefixedFamilyPath()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """
            [
              {
                "product": {
                  "id": 1,
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
            """));
        var client = CreateClient(handler);

        var products = await client.ListProductsForProductFamilyAsync("eshop-subscribe");

        Assert.Equal("product_families/handle:eshop-subscribe/products.json?page=1&per_page=200&include_archived=false",
            handler.LastRequest!.RequestUri!.PathAndQuery.TrimStart('/'));
        var product = Assert.Single(products);
        Assert.Equal("eshop-pro", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
    }

    [Fact]
    public async Task ReadCustomerByReferenceAsync_ReturnsNullOn404()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "Not Found"));
        var client = CreateClient(handler);

        var customer = await client.ReadCustomerByReferenceAsync("buyer-1");

        Assert.Null(customer);
        Assert.Contains("customers/lookup.json?reference=buyer-1", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task CreateSubscriptionAsync_PostsSnakeCasePayload()
    {
        string? body = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return StubHttpMessageHandler.Json(HttpStatusCode.Created, """
                {
                  "subscription": {
                    "id": 88,
                    "state": "active",
                    "product_price_in_cents": 2900,
                    "next_assessment_at": "2026-09-19T12:00:00-04:00",
                    "product": { "handle": "basic-plan", "name": "Basic Plan" }
                  }
                }
                """);
        });
        var client = CreateClient(handler);

        var created = await client.CreateSubscriptionAsync(new CreateSubscription
        {
            ProductHandle = "basic-plan",
            CustomerId = 42,
            Reference = "eshop:buyer-1:basic-plan",
            PaymentCollectionMethod = "remittance"
        });

        Assert.Equal(88, created.Id);
        Assert.Equal("active", created.State);
        Assert.Contains("\"product_handle\":\"basic-plan\"", body);
        Assert.Contains("\"customer_id\":42", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
        Assert.Equal("/subscriptions.json", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    private static MaxioAdvancedBillingClient CreateClient(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.chargify.com/")
        };
        return new MaxioAdvancedBillingClient(httpClient);
    }
}
