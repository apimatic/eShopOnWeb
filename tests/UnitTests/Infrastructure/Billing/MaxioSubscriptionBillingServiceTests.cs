using System.Net;
using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly ShopperIdentity Shopper = new("user-1", "ann@example.com", "Ann", "Buyer");

    [Fact]
    public async Task ListPlans_ReturnsProductsInConfiguredFamily()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, """
            [
              {
                "product": {
                  "id": 1,
                  "name": "Pro Plan",
                  "handle": "eshop-pro",
                  "description": "Pro",
                  "price_in_cents": 29900,
                  "interval": 1,
                  "interval_unit": "month",
                  "product_family": { "handle": "eshop-subscribe" }
                }
              },
              {
                "product": {
                  "id": 2,
                  "name": "Other",
                  "handle": "other-plan",
                  "price_in_cents": 1000,
                  "interval": 1,
                  "interval_unit": "month",
                  "product_family": { "handle": "someone-else" }
                }
              }
            ]
            """));

        var service = CreateService(handler);

        var plans = await service.ListPlansAsync(CancellationToken.None);

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(299.00m, plan.Price);
        Assert.Contains("/products", handler.Requests[0].RequestUri!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Subscribe_ReturnsExistingLiveSubscriptionWithoutCreating()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("subscription", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, SubscriptionJson(99, "eshop-pro", "active"));
            }

            return StubHandler.Json(HttpStatusCode.InternalServerError, """{"errors":["unexpected"]}""");
        });

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Shopper, "eshop-pro", CancellationToken.None);

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal(299.00m, result.Price);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerThenSubscription()
    {
        var handler = new StubHandler(request => Dispatch(request,
            onFindSubscription: () => new HttpResponseMessage(HttpStatusCode.NotFound),
            onCustomerLookup: () => new HttpResponseMessage(HttpStatusCode.NotFound),
            onCreateCustomer: () => StubHandler.Json(HttpStatusCode.Created, """
                { "customer": { "id": 10, "reference": "user-1", "email": "ann@example.com", "first_name": "Ann", "last_name": "Buyer" } }
                """),
            onListCustomerSubscriptions: () => StubHandler.Json(HttpStatusCode.OK, "[]"),
            onCreateSubscription: () => StubHandler.Json(HttpStatusCode.Created, SubscriptionJson(42, "eshop-pro", "active"))));

        var pipeline = new SingleFlightWriteHandler { InnerHandler = handler };
        var service = CreateService(pipeline);

        var result = await service.SubscribeAsync(Shopper, "eshop-pro", CancellationToken.None);

        Assert.Equal(42, result.Id);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Equal("active", result.State);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && IsCustomersPath(r) && !IsCustomerSubscriptionsPath(r));
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && IsSubscriptionsPath(r) && !IsCustomerSubscriptionsPath(r));
    }

    [Fact]
    public async Task Subscribe_MapsValidationFailureTo422()
    {
        var handler = new StubHandler(request => Dispatch(request,
            onFindSubscription: () => new HttpResponseMessage(HttpStatusCode.NotFound),
            onCustomerLookup: () => StubHandler.Json(HttpStatusCode.OK, """
                { "customer": { "id": 10, "reference": "user-1", "email": "ann@example.com", "first_name": "Ann", "last_name": "Buyer" } }
                """),
            onListCustomerSubscriptions: () => StubHandler.Json(HttpStatusCode.OK, "[]"),
            onCreateSubscription: () => StubHandler.Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["product handle is invalid"] }""")));

        var pipeline = new SingleFlightWriteHandler { InnerHandler = handler };
        var service = CreateService(pipeline);

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Shopper, "no-such-plan", CancellationToken.None));

        Assert.Equal(422, ex.StatusCode);
        Assert.DoesNotContain("SdkException", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxioAdvancedBilling", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Subscribe_DoesNotResendPostAfterTransportFailure()
    {
        var posts = 0;
        var handler = new StubHandler(request => Dispatch(request,
            onFindSubscription: () => new HttpResponseMessage(HttpStatusCode.NotFound),
            onCustomerLookup: () => StubHandler.Json(HttpStatusCode.OK, """
                { "customer": { "id": 10, "reference": "user-1", "email": "ann@example.com", "first_name": "Ann", "last_name": "Buyer" } }
                """),
            onListCustomerSubscriptions: () => StubHandler.Json(HttpStatusCode.OK, "[]"),
            onCreateSubscription: () =>
            {
                posts++;
                throw new HttpRequestException("connection reset");
            }));

        var pipeline = new SingleFlightWriteHandler { InnerHandler = handler };
        var service = CreateService(pipeline);

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Shopper, "eshop-pro", CancellationToken.None));

        Assert.Equal(1, posts);
        Assert.True(ex.StatusCode is 502 or 503 or 504);
    }

    private static MaxioSubscriptionBillingService CreateService(HttpMessageHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        });

        return new MaxioSubscriptionBillingService(client, options, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static HttpResponseMessage Dispatch(
        HttpRequestMessage request,
        Func<HttpResponseMessage>? onFindSubscription = null,
        Func<HttpResponseMessage>? onCustomerLookup = null,
        Func<HttpResponseMessage>? onCreateCustomer = null,
        Func<HttpResponseMessage>? onListCustomerSubscriptions = null,
        Func<HttpResponseMessage>? onCreateSubscription = null)
    {
        if (request.Method == HttpMethod.Get && IsCustomerSubscriptionsPath(request))
        {
            return onListCustomerSubscriptions?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (request.Method == HttpMethod.Get && IsCustomersPath(request))
        {
            return onCustomerLookup?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (request.Method == HttpMethod.Get && IsSubscriptionsPath(request))
        {
            return onFindSubscription?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (request.Method == HttpMethod.Post && IsCustomersPath(request) && !IsCustomerSubscriptionsPath(request))
        {
            return onCreateCustomer?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (request.Method == HttpMethod.Post && IsSubscriptionsPath(request))
        {
            return onCreateSubscription?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            ReasonPhrase = $"{request.Method} {request.RequestUri}"
        };
    }

    private static bool IsCustomerSubscriptionsPath(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        return path.Contains("customer", StringComparison.OrdinalIgnoreCase)
            && path.Contains("subscription", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCustomersPath(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.Contains("customer", StringComparison.OrdinalIgnoreCase);

    private static bool IsSubscriptionsPath(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.Contains("subscription", StringComparison.OrdinalIgnoreCase);

    private static string SubscriptionJson(int id, string handle, string state) => $$"""
        {
          "subscription": {
            "id": {{id}},
            "state": "{{state}}",
            "product_price_in_cents": 29900,
            "current_period_ends_at": "2026-09-20T00:00:00Z",
            "next_assessment_at": "2026-09-20T00:00:00Z",
            "product": {
              "id": 1,
              "name": "Pro Plan",
              "handle": "{{handle}}",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month"
            }
          }
        }
        """;
}
