using System.Net;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string BuyerId = "buyer-1";
    private const string FamilyHandle = "eshop-subscribe";
    private const string ProHandle = "eshop-pro";

    private static readonly SubscribeCommand Subscribe = new()
    {
        BuyerId = BuyerId,
        Email = "buyer@example.com",
        FirstName = "buyer",
        LastName = "Customer",
        ProductHandle = ProHandle
    };

    [Fact]
    public async Task ListPlansAsync_ReturnsMappedPlans()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, ProProductListJson));
        var service = CreateService(handler);

        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal(ProHandle, plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task ListPlansAsync_UnreadableSuccessBody_ThrowsSanitizedError()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, "{}"));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.ListPlansAsync());

        Assert.Equal(502, ex.StatusCode);
        Assert.Equal(BillingFailureKind.UnreadableSuccess, ex.Kind);
        Assert.DoesNotContain("Json", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/products/handle", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, ProProductJson);
            }

            if (request.Method == HttpMethod.Get && path.Contains("customers", StringComparison.OrdinalIgnoreCase)
                && request.RequestUri!.Query.Contains("reference", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Empty(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Post && path.Contains("customers", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.Created, CustomerJson);
            }

            if (request.Method == HttpMethod.Get && path.Contains("subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Empty(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Post && path.Contains("subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.Created, SubscriptionJson);
            }

            return StubHandler.Empty(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Subscribe);

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal(ProHandle, result.ProductHandle);
        Assert.Equal(299.00m, result.Price);
        Assert.NotNull(result.CurrentPeriodEndsAt);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("customers", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("subscriptions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SubscribeAsync_WhenSubscriptionExists_DoesNotCreateAnother()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/products/handle", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, ProProductJson);
            }

            if (request.Method == HttpMethod.Get && path.Contains("customers", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, CustomerJson);
            }

            if (request.Method == HttpMethod.Get && path.Contains("subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, SubscriptionJson);
            }

            return StubHandler.Json(HttpStatusCode.InternalServerError, """{"errors":["should not create"]}""");
        });

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Subscribe);

        Assert.Equal(99, result.Id);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeAsync_UnknownPlan_ReturnsNotFound()
    {
        var handler = new StubHandler(_ => StubHandler.Empty(HttpStatusCode.NotFound));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(Subscribe));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal(BillingFailureKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task SubscribeAsync_ValidationError_SurfacesClientError()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/products/handle", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, ProProductJson);
            }

            if (request.Method == HttpMethod.Get && path.Contains("customers", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, CustomerJson);
            }

            if (request.Method == HttpMethod.Get && path.Contains("subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Empty(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Post && path.Contains("subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Product must be specified"]}""");
            }

            return StubHandler.Empty(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(Subscribe));

        Assert.Equal(422, ex.StatusCode);
        Assert.Equal(BillingFailureKind.ClientError, ex.Kind);
        Assert.Contains("Product must be specified", ex.Message);
    }

    [Fact]
    public async Task ListSubscriptionsForBuyerAsync_WhenCustomerMissing_ReturnsEmpty()
    {
        var handler = new StubHandler(_ => StubHandler.Empty(HttpStatusCode.NotFound));
        var service = CreateService(handler);

        var result = await service.ListSubscriptionsForBuyerAsync(BuyerId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListSubscriptionsForBuyerAsync_MapsSubscriptions()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("customers", StringComparison.OrdinalIgnoreCase)
                && request.RequestUri!.Query.Contains("reference", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, CustomerJson);
            }

            if (request.Method == HttpMethod.Get && path.Contains("subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, "[" + SubscriptionJson + "]");
            }

            return StubHandler.Empty(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.ListSubscriptionsForBuyerAsync(BuyerId);

        var subscription = Assert.Single(result);
        Assert.Equal(99, subscription.Id);
        Assert.Equal("active", subscription.State);
    }

    [Fact]
    public async Task ListPlansAsync_WhenNotConfigured_Throws()
    {
        var handler = new StubHandler(_ => StubHandler.Empty(HttpStatusCode.OK));
        var service = CreateService(handler, new MaxioOptions());

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.ListPlansAsync());

        Assert.Equal(503, ex.StatusCode);
    }

    private static MaxioSubscriptionBillingService CreateService(StubHandler handler, MaxioOptions? options = null)
    {
        options ??= new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "testsite",
            ProductFamilyHandle = FamilyHandle
        };

        var client = new MaxioAdvancedBillingClient(
            new HttpClient(handler),
            MaxioServiceCollectionExtensions.CreateClientOptions(options));

        return new MaxioSubscriptionBillingService(
            client,
            Options.Create(options),
            Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>());
    }

    private const string ProProductJson = """
        {
          "product": {
            "id": 1,
            "name": "Pro Plan",
            "handle": "eshop-pro",
            "description": "Pro",
            "price_in_cents": 29900,
            "interval": 1,
            "interval_unit": "month",
            "product_family": { "id": 10, "handle": "eshop-subscribe", "name": "eShop Subscribe" }
          }
        }
        """;

    private const string ProProductListJson = "[" + ProProductJson + "]";

    private const string CustomerJson = """
        {
          "customer": {
            "id": 10,
            "reference": "buyer-1",
            "email": "buyer@example.com",
            "first_name": "buyer",
            "last_name": "Customer"
          }
        }
        """;

    private const string SubscriptionJson = """
        {
          "subscription": {
            "id": 99,
            "state": "active",
            "product_price_in_cents": 29900,
            "current_period_ends_at": "2026-09-19T00:00:00Z",
            "reference": "buyer-1:eshop-pro",
            "product": {
              "id": 1,
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
