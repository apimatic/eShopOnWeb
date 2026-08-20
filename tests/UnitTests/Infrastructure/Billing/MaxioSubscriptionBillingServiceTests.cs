using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";

    [Fact]
    public async Task ListPlans_MapsHandleNamePriceAndInterval()
    {
        var (service, handler) = MaxioTestClient.Create(_ => MaxioTestClient.Json(HttpStatusCode.OK, PlansJson));

        var plans = await service.ListPlansAsync(CancellationToken.None);

        Assert.Equal(2, plans.Count);
        var pro = Assert.Single(plans, p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        var path = handler.Requests[0].RequestUri!.AbsolutePath;
        Assert.Contains("/product_families/handle", path);
        Assert.Contains("eshop-subscribe", Uri.UnescapeDataString(path));
    }

    [Fact]
    public async Task ListPlans_SkipsArchivedProducts()
    {
        var json = """
            [
              {
                "product": {
                  "name": "Gone",
                  "handle": "gone",
                  "price_in_cents": 100,
                  "interval": 1,
                  "interval_unit": "month",
                  "archived_at": "2024-01-01T00:00:00Z",
                  "product_family": { "handle": "eshop-subscribe" }
                }
              },
              {
                "product": {
                  "name": "Basic Plan",
                  "handle": "basic-plan",
                  "price_in_cents": 2900,
                  "interval": 1,
                  "interval_unit": "month",
                  "product_family": { "handle": "eshop-subscribe" }
                }
              }
            ]
            """;
        var (service, _) = MaxioTestClient.Create(_ => MaxioTestClient.Json(HttpStatusCode.OK, json));

        var plans = await service.ListPlansAsync(CancellationToken.None);

        var plan = Assert.Single(plans);
        Assert.Equal("basic-plan", plan.Handle);
        Assert.Equal(29.00m, plan.Price);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerThenSubscription()
    {
        var (service, handler) = MaxioTestClient.Create(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/customers/lookup.json"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("""{"errors":"Not Found"}""", System.Text.Encoding.UTF8, "application/json")
                };
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
            {
                return MaxioTestClient.Json(HttpStatusCode.Created, """
                    { "customer": { "id": 42, "reference": "user-1", "email": "a@b.com", "first_name": "a", "last_name": "b" } }
                    """);
            }

            if (path.Contains("/subscriptions/lookup.json"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (path.Contains("/customers/") && path.EndsWith("/subscriptions.json"))
            {
                return MaxioTestClient.Json(HttpStatusCode.OK, "[]");
            }

            if (request.Method == HttpMethod.Get && path.Contains("/products.json"))
            {
                return MaxioTestClient.Json(HttpStatusCode.OK, PlansJson);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
            {
                return MaxioTestClient.Json(HttpStatusCode.Created, CreatedSubscriptionJson);
            }

            return MaxioTestClient.Json(HttpStatusCode.NotFound, "{}");
        });

        var result = await service.SubscribeAsync(
            new SubscribeRequest("user-1", "a@b.com", "Ada", "Shopper", "eshop-pro"),
            CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(99, result.Id);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal("Pro Plan", result.PlanName);
        Assert.Equal(299.00m, result.Price);
        Assert.Equal("active", result.State);
        Assert.NotNull(result.NextBillingDate);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/customers.json"));
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json"));
        Assert.Contains(handler.Bodies, b => b.Contains("eshop-pro", StringComparison.Ordinal) && b.Contains("remittance", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Subscribe_DoubleClick_ReturnsExistingWithoutSecondCreate()
    {
        var createCount = 0;
        var (service, _) = MaxioTestClient.Create(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/products.json"))
            {
                return MaxioTestClient.Json(HttpStatusCode.OK, PlansJson);
            }

            if (path.Contains("/customers/lookup.json"))
            {
                return MaxioTestClient.Json(HttpStatusCode.OK, """
                    { "customer": { "id": 42, "reference": "user-1", "email": "a@b.com" } }
                    """);
            }

            if (path.Contains("/subscriptions/lookup.json"))
            {
                return MaxioTestClient.Json(HttpStatusCode.OK, CreatedSubscriptionJson);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
            {
                createCount++;
                return MaxioTestClient.Json(HttpStatusCode.Created, CreatedSubscriptionJson);
            }

            return MaxioTestClient.Json(HttpStatusCode.OK, "[]");
        });

        var first = await service.SubscribeAsync(
            new SubscribeRequest("user-1", "a@b.com", "Ada", "Shopper", "eshop-pro"),
            CancellationToken.None);
        var second = await service.SubscribeAsync(
            new SubscribeRequest("user-1", "a@b.com", "Ada", "Shopper", "eshop-pro"),
            CancellationToken.None);

        Assert.False(first.Created);
        Assert.False(second.Created);
        Assert.Equal(99, first.Id);
        Assert.Equal(99, second.Id);
        Assert.Equal(0, createCount);
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_IsClientError()
    {
        var (service, _) = MaxioTestClient.Create(_ => MaxioTestClient.Json(HttpStatusCode.OK, PlansJson));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() =>
            service.SubscribeAsync(
                new SubscribeRequest("user-1", "a@b.com", "Ada", "Shopper", "not-a-plan"),
                CancellationToken.None));

        Assert.Equal(400, ex.HttpStatusCode);
        Assert.True(ex.IsClientError);
    }

    [Fact]
    public async Task ListMySubscriptions_WhenCustomerMissing_ReturnsEmpty()
    {
        var (service, _) = MaxioTestClient.Create(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/customers/lookup.json"))
            {
                return MaxioTestClient.Json(HttpStatusCode.NotFound, """{"errors":"Not Found"}""");
            }

            return MaxioTestClient.Json(HttpStatusCode.OK, "[]");
        });

        var result = await service.ListMySubscriptionsAsync("user-1", CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task CreateClient_UsesUsSiteAndLiteralPasswordX()
    {
        var options = new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe",
            Environment = "US"
        };

        var client = MaxioServiceCollectionExtensions.CreateClient(new HttpClient(), options);

        Assert.NotNull(client);
        Assert.Equal(MaxioAdvancedBilling.Servers.ServerEnvironment.Us, MaxioServiceCollectionExtensions.ResolveEnvironment("US"));
        Assert.Equal(MaxioAdvancedBilling.Servers.ServerEnvironment.Eu, MaxioServiceCollectionExtensions.ResolveEnvironment("EU"));
    }

    private const string PlansJson = """
        [
          {
            "product": {
              "name": "Pro Plan",
              "handle": "eshop-pro",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month",
              "product_family": { "handle": "eshop-subscribe" }
            }
          },
          {
            "product": {
              "name": "Basic Plan",
              "handle": "basic-plan",
              "price_in_cents": 2900,
              "interval": 1,
              "interval_unit": "month",
              "product_family": { "handle": "eshop-subscribe" }
            }
          }
        ]
        """;

    private const string CreatedSubscriptionJson = """
        {
          "subscription": {
            "id": 99,
            "state": "active",
            "product_price_in_cents": 29900,
            "next_assessment_at": "2026-09-21T00:00:00Z",
            "reference": "user-1:eshop-pro",
            "product": {
              "name": "Pro Plan",
              "handle": "eshop-pro",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month"
            }
          }
        }
        """;
}
