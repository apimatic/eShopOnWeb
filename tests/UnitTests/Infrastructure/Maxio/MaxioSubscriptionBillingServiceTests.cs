using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private static readonly SubscriberInfo Subscriber = SubscriberInfo.FromIdentity("demouser@microsoft.com", "demouser@microsoft.com");
    private static readonly string ExpectedReference = "eshop-user-demouser@microsoft.com";

    private const string FamiliesJson = "[{\"product_family\":{\"id\":1,\"name\":\"eShopSubscribe\",\"handle\":\"eshop-subscribe\"}}]";
    private const string ProductsJson =
        "[{\"product\":{\"id\":7130997,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\",\"product_family\":{\"id\":1,\"handle\":\"eshop-subscribe\"}}}," +
        "{\"product\":{\"id\":7130998,\"name\":\"Basic Plan\",\"handle\":\"basic-plan\",\"price_in_cents\":2900,\"interval\":1,\"interval_unit\":\"month\",\"product_family\":{\"id\":1,\"handle\":\"eshop-subscribe\"}}}]";
    private const string CustomerJson =
        "{\"customer\":{\"id\":555,\"first_name\":\"demouser\",\"last_name\":\"eShopOnWeb\",\"email\":\"demouser@microsoft.com\",\"reference\":\"eshop-user-demouser@microsoft.com\"}}";

    private static MaxioSubscriptionBillingService CreateService(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.chargify.com/") };
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test",
            ProductFamilyHandle = FamilyHandle
        });
        var logger = Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>();
        return new MaxioSubscriptionBillingService(httpClient, settings, logger);
    }

    private static string SubscriptionJson(long id, string state, string handle, long priceInCents) =>
        $"{{\"subscription\":{{\"id\":{id},\"state\":\"{state}\",\"current_period_ends_at\":\"2026-09-16T00:00:00+00:00\",\"next_assessment_at\":\"2026-09-16T00:00:00+00:00\"," +
        $"\"product\":{{\"id\":7130997,\"name\":\"Pro Plan\",\"handle\":\"{handle}\",\"price_in_cents\":{priceInCents}}}," +
        "\"customer\":{\"id\":555,\"reference\":\"eshop-user-demouser@microsoft.com\"}}}";

    [Fact]
    public async Task GetPlansAsync_ReturnsProductsOfConfiguredFamily_SortedByPrice()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/product_families.json") return (HttpStatusCode.OK, FamiliesJson);
            if (path == "/product_families/1/products.json") return (HttpStatusCode.OK, ProductsJson);
            return (HttpStatusCode.NotFound, "{}");
        });

        var plans = await CreateService(handler).GetPlansAsync(CancellationToken.None);

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle); // cheapest first
        Assert.Equal("eshop-pro", plans[1].Handle);

        var pro = plans[1];
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299m, pro.Price);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.Equal("eshop-subscribe", pro.ProductFamilyHandle);
    }

    [Fact]
    public async Task SubscribeAsync_WhenNoCustomer_CreatesCustomerThenSubscription_WithoutCard()
    {
        string? subscriptionRequestBody = null;
        var handler = new FakeHttpMessageHandler((request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/product_families.json") return (HttpStatusCode.OK, FamiliesJson);
            if (path == "/product_families/1/products.json") return (HttpStatusCode.OK, ProductsJson);
            if (path == "/customers/lookup.json") return (HttpStatusCode.NotFound, "{}"); // no customer yet
            if (path == "/customers.json" && request.Method == HttpMethod.Post) return (HttpStatusCode.Created, CustomerJson);
            if (path == "/customers/555/subscriptions.json") return (HttpStatusCode.OK, "[]"); // no existing subs
            if (path == "/subscriptions.json" && request.Method == HttpMethod.Post)
            {
                subscriptionRequestBody = body;
                return (HttpStatusCode.Created, SubscriptionJson(93849898, "active", "eshop-pro", 29900));
            }
            return (HttpStatusCode.InternalServerError, "{}");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(Subscriber, "eshop-pro", CancellationToken.None);

        Assert.Equal(93849898, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal(29900, result.PriceInCents);
        Assert.Equal(ExpectedReference, result.CustomerReference);
        Assert.NotNull(result.NextBillingDate);

        // Exactly one customer and one subscription were created.
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));

        // No card / payment profile is sent; remittance collection is requested and the customer is
        // referenced by its id.
        Assert.NotNull(subscriptionRequestBody);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", subscriptionRequestBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", subscriptionRequestBody);
        Assert.Contains("\"customer_id\":555", subscriptionRequestBody);
        Assert.DoesNotContain("credit_card", subscriptionRequestBody);
        Assert.DoesNotContain("payment_profile", subscriptionRequestBody);
    }

    [Fact]
    public async Task SubscribeAsync_WhenExistingCustomer_DoesNotCreateAnother()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/product_families.json") return (HttpStatusCode.OK, FamiliesJson);
            if (path == "/product_families/1/products.json") return (HttpStatusCode.OK, ProductsJson);
            if (path == "/customers/lookup.json") return (HttpStatusCode.OK, CustomerJson); // already exists
            if (path == "/customers/555/subscriptions.json") return (HttpStatusCode.OK, "[]");
            if (path == "/subscriptions.json" && request.Method == HttpMethod.Post)
                return (HttpStatusCode.Created, SubscriptionJson(93849898, "active", "eshop-pro", 29900));
            return (HttpStatusCode.InternalServerError, "{}");
        });

        var service = CreateService(handler);
        await service.SubscribeAsync(Subscriber, "eshop-pro", CancellationToken.None);

        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenLiveSubscriptionToSamePlanExists_ReusesItAndDoesNotCreate()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/product_families.json") return (HttpStatusCode.OK, FamiliesJson);
            if (path == "/product_families/1/products.json") return (HttpStatusCode.OK, ProductsJson);
            if (path == "/customers/lookup.json") return (HttpStatusCode.OK, CustomerJson);
            if (path == "/customers/555/subscriptions.json")
                return (HttpStatusCode.OK, "[" + SubscriptionJson(42, "active", "eshop-pro", 29900) + "]");
            if (path == "/subscriptions.json" && request.Method == HttpMethod.Post)
                return (HttpStatusCode.Created, SubscriptionJson(999, "active", "eshop-pro", 29900));
            return (HttpStatusCode.InternalServerError, "{}");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(Subscriber, "eshop-pro", CancellationToken.None);

        Assert.Equal(42, result.Id); // reused the existing one, not the "newly created" 999
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenCanceledSubscriptionExists_CreatesNewOne()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/product_families.json") return (HttpStatusCode.OK, FamiliesJson);
            if (path == "/product_families/1/products.json") return (HttpStatusCode.OK, ProductsJson);
            if (path == "/customers/lookup.json") return (HttpStatusCode.OK, CustomerJson);
            if (path == "/customers/555/subscriptions.json")
                return (HttpStatusCode.OK, "[" + SubscriptionJson(42, "canceled", "eshop-pro", 29900) + "]");
            if (path == "/subscriptions.json" && request.Method == HttpMethod.Post)
                return (HttpStatusCode.Created, SubscriptionJson(999, "active", "eshop-pro", 29900));
            return (HttpStatusCode.InternalServerError, "{}");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(Subscriber, "eshop-pro", CancellationToken.None);

        Assert.Equal(999, result.Id); // canceled is terminal, so a fresh subscription is created
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenPlanNotInFamily_ThrowsPlanNotFound()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/product_families.json") return (HttpStatusCode.OK, FamiliesJson);
            if (path == "/product_families/1/products.json") return (HttpStatusCode.OK, ProductsJson);
            return (HttpStatusCode.InternalServerError, "{}");
        });

        var service = CreateService(handler);
        await Assert.ThrowsAsync<PlanNotFoundException>(() =>
            service.SubscribeAsync(Subscriber, "no-such-plan", CancellationToken.None));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_WhenNoCustomer_ReturnsEmpty()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/customers/lookup.json") return (HttpStatusCode.NotFound, "{}");
            return (HttpStatusCode.InternalServerError, "{}");
        });

        var service = CreateService(handler);
        var result = await service.GetSubscriptionsAsync(Subscriber, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_MapsNextBillingDateFromCurrentPeriodEndsAt()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/customers/lookup.json") return (HttpStatusCode.OK, CustomerJson);
            if (path == "/customers/555/subscriptions.json")
                return (HttpStatusCode.OK, "[" + SubscriptionJson(42, "active", "eshop-pro", 29900) + "]");
            return (HttpStatusCode.InternalServerError, "{}");
        });

        var service = CreateService(handler);
        var result = await service.GetSubscriptionsAsync(Subscriber, CancellationToken.None);

        var sub = Assert.Single(result);
        Assert.Equal(new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero), sub.NextBillingDate);
        Assert.Equal("active", sub.State);
    }
}
