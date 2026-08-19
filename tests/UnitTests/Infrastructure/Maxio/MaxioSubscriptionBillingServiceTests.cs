using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private const string UserId = "user-1";
    private const string FamilyHandle = "eshop-subscribe";

    [Fact]
    public async Task ListPlans_MapsHandleNamePriceAndInterval()
    {
        var handler = new StubHandler(_ => StubResponses.Json(HttpStatusCode.OK, """
            [
              {
                "product": {
                  "handle": "eshop-pro",
                  "name": "Pro Plan",
                  "description": "Monthly pro",
                  "price_in_cents": 29900,
                  "interval": 1,
                  "interval_unit": "month",
                  "require_credit_card": false,
                  "product_family": { "handle": "eshop-subscribe" }
                }
              }
            ]
            """));
        var service = CreateService(handler);

        var plans = await service.ListPlansAsync(default);

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal("Monthly pro", plan.Description);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(plan.RequireCreditCard);
        var listUri = Uri.UnescapeDataString(handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Contains("eshop-subscribe", listUri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("product_famil", listUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerThenSubscriptionWithoutPaymentFields()
    {
        string? createSubBody = null;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/customers/lookup", StringComparison.OrdinalIgnoreCase))
            {
                return StubResponses.Json(HttpStatusCode.NotFound, """{"errors":["not found"]}""");
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json", StringComparison.OrdinalIgnoreCase))
            {
                return StubResponses.Json(HttpStatusCode.Created, """
                    { "customer": { "id": 42, "reference": "user-1", "email": "a@b.com", "first_name": "a", "last_name": "eShopOnWeb" } }
                    """);
            }

            if (path.Contains("/customers/42/subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return StubResponses.Json(HttpStatusCode.OK, "[]");
            }

            if (request.Method == HttpMethod.Post && path.Contains("/subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                createSubBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return StubResponses.Json(HttpStatusCode.Created, """
                    {
                      "subscription": {
                        "id": 99,
                        "state": "active",
                        "product_price_in_cents": 29900,
                        "next_assessment_at": "2026-09-20T00:00:00Z",
                        "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 }
                      }
                    }
                    """);
            }

            return StubResponses.Json(HttpStatusCode.NotFound, "{}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(UserId, "a@b.com", "a", "eShopOnWeb", "eshop-pro", default);

        Assert.Equal(99, result.Id);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Equal("Pro Plan", result.ProductName);
        Assert.Equal(299.00m, result.Price);
        Assert.Equal("active", result.State);
        Assert.NotNull(result.NextBillingDate);
        Assert.False(string.IsNullOrEmpty(createSubBody));
        var compact = createSubBody!.Replace(" ", string.Empty);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", compact);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", compact);
        Assert.DoesNotContain("chargify_token", createSubBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payment_profile", createSubBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credit_card", createSubBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Subscribe_ReturnsExistingLiveSubscriptionWithoutCreatingAnother()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/customers/lookup", StringComparison.OrdinalIgnoreCase))
            {
                return StubResponses.Json(HttpStatusCode.OK, """
                    { "customer": { "id": 42, "reference": "user-1" } }
                    """);
            }

            if (path.Contains("/customers/42/subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return StubResponses.Json(HttpStatusCode.OK, """
                    [
                      {
                        "subscription": {
                          "id": 77,
                          "state": "active",
                          "product_price_in_cents": 29900,
                          "next_assessment_at": "2026-09-20T00:00:00Z",
                          "product": { "handle": "eshop-pro", "name": "Pro Plan" }
                        }
                      }
                    ]
                    """);
            }

            return StubResponses.Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(UserId, "a@b.com", "a", "eShopOnWeb", "eshop-pro", default);

        Assert.Equal(77, result.Id);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ListMySubscriptions_ReturnsEmptyWhenCustomerMissing()
    {
        var handler = new StubHandler(_ => StubResponses.Json(HttpStatusCode.NotFound, """{"errors":["not found"]}"""));
        var service = CreateService(handler);

        var result = await service.ListMySubscriptionsAsync(UserId, default);

        Assert.Empty(result);
    }

    private static MaxioSubscriptionBillingService CreateService(StubHandler handler)
    {
        var http = new HttpClient(handler);
        var settings = new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "cp-exp-1",
            ProductFamilyHandle = FamilyHandle
        };
        var client = MaxioServiceCollectionExtensions.CreateClient(http, settings, "US");
        var logger = Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>();
        return new MaxioSubscriptionBillingService(client, Options.Create(settings), logger);
    }
}
