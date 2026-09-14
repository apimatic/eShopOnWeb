using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly SubscriberIdentity Demouser =
        new("demouser@microsoft.com", "demouser@microsoft.com");

    /// <summary>Routes a Maxio call to a canned response by method and path.</summary>
    private static StubResponse Route(HttpRequestMessage request, Func<string, StubResponse> byPath) =>
        byPath($"{request.Method.Method} {request.RequestUri!.AbsolutePath}");

    [Fact]
    public async Task GetPlans_projects_active_products_from_the_configured_family_cheapest_first()
    {
        var context = new MaxioTestContext((request, _) => Route(request, path => path switch
        {
            "GET /product_families/handle:test-family/products.json" => StubResponse.Ok(MaxioPayloads.Products),
            _ => throw new InvalidOperationException(path)
        }));

        var plans = await context.Service.GetPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(p => p.Handle));
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("299.00", pro.FormattedPrice);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentMethod);
    }

    [Fact]
    public async Task Subscribe_creates_the_customer_then_the_subscription()
    {
        var customerCreated = false;

        var context = new MaxioTestContext((request, _) => Route(request, path => path switch
        {
            "GET /site.json" => StubResponse.Ok(MaxioPayloads.Site),
            "GET /product_families/handle:test-family/products.json" => StubResponse.Ok(MaxioPayloads.Products),
            "GET /customers/lookup.json" => customerCreated ? StubResponse.Ok(MaxioPayloads.Customer) : StubResponse.NotFound(),
            "POST /customers.json" => Mark(() => customerCreated = true, StubResponse.Ok(MaxioPayloads.Customer)),
            "GET /customers/555/subscriptions.json" => StubResponse.Ok(MaxioPayloads.NoSubscriptions),
            "POST /subscriptions.json" => StubResponse.Created(MaxioPayloads.ProSubscription),
            _ => throw new InvalidOperationException(path)
        }));

        var result = await context.Service.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        Assert.True(result.Created);
        Assert.Equal(777, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.True(result.Subscription.IsLive);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal("299.00", result.Subscription.FormattedPrice);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingAt);
        Assert.Equal(1, context.Handler.CountOf("POST", "/customers.json"));
        Assert.Equal(1, context.Handler.CountOf("POST", "/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_reuses_an_existing_customer_instead_of_creating_a_second_one()
    {
        var context = new MaxioTestContext((request, _) => Route(request, path => path switch
        {
            "GET /site.json" => StubResponse.Ok(MaxioPayloads.Site),
            "GET /product_families/handle:test-family/products.json" => StubResponse.Ok(MaxioPayloads.Products),
            "GET /customers/lookup.json" => StubResponse.Ok(MaxioPayloads.Customer),
            "GET /customers/555/subscriptions.json" => StubResponse.Ok(MaxioPayloads.NoSubscriptions),
            "POST /subscriptions.json" => StubResponse.Created(MaxioPayloads.ProSubscription),
            _ => throw new InvalidOperationException(path)
        }));

        await context.Service.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        Assert.Equal(0, context.Handler.CountOf("POST", "/customers.json"));
    }

    [Fact]
    public async Task Subscribe_returns_the_existing_subscription_without_creating_a_second_one()
    {
        var context = new MaxioTestContext((request, _) => Route(request, path => path switch
        {
            "GET /site.json" => StubResponse.Ok(MaxioPayloads.Site),
            "GET /product_families/handle:test-family/products.json" => StubResponse.Ok(MaxioPayloads.Products),
            "GET /customers/lookup.json" => StubResponse.Ok(MaxioPayloads.Customer),
            "GET /customers/555/subscriptions.json" => StubResponse.Ok(MaxioPayloads.ProSubscriptionList),
            _ => throw new InvalidOperationException(path)
        }));

        var result = await context.Service.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        Assert.False(result.Created);
        Assert.Equal(777, result.Subscription.Id);
        Assert.Equal(0, context.Handler.CountOf("POST", "/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_allows_resubscribing_after_the_previous_subscription_ended()
    {
        var context = new MaxioTestContext((request, _) => Route(request, path => path switch
        {
            "GET /site.json" => StubResponse.Ok(MaxioPayloads.Site),
            "GET /product_families/handle:test-family/products.json" => StubResponse.Ok(MaxioPayloads.Products),
            "GET /customers/lookup.json" => StubResponse.Ok(MaxioPayloads.Customer),
            "GET /customers/555/subscriptions.json" => StubResponse.Ok(MaxioPayloads.CanceledProSubscriptionList),
            "POST /subscriptions.json" => StubResponse.Created(MaxioPayloads.ProSubscription),
            _ => throw new InvalidOperationException(path)
        }));

        var result = await context.Service.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        Assert.True(result.Created);
    }

    [Fact]
    public async Task Subscribe_recovers_the_subscription_Maxio_already_created_when_a_duplicate_is_rejected()
    {
        // First read shows nothing, the create is rejected as a duplicate, and the follow-up read
        // finds what the winning request produced. This is the lost-response recovery path.
        var subscriptionReads = 0;

        var context = new MaxioTestContext((request, _) => Route(request, path => path switch
        {
            "GET /site.json" => StubResponse.Ok(MaxioPayloads.Site),
            "GET /product_families/handle:test-family/products.json" => StubResponse.Ok(MaxioPayloads.Products),
            "GET /customers/lookup.json" => StubResponse.Ok(MaxioPayloads.Customer),
            "GET /customers/555/subscriptions.json" => ++subscriptionReads == 1
                ? StubResponse.Ok(MaxioPayloads.NoSubscriptions)
                : StubResponse.Ok(MaxioPayloads.ProSubscriptionList),
            "POST /subscriptions.json" => StubResponse.Duplicate(),
            _ => throw new InvalidOperationException(path)
        }));

        var result = await context.Service.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        Assert.False(result.Created);
        Assert.Equal(777, result.Subscription.Id);
    }

    [Fact]
    public async Task Subscribe_reports_a_conflict_when_a_duplicate_cannot_be_resolved()
    {
        var context = new MaxioTestContext((request, _) => Route(request, path => path switch
        {
            "GET /site.json" => StubResponse.Ok(MaxioPayloads.Site),
            "GET /product_families/handle:test-family/products.json" => StubResponse.Ok(MaxioPayloads.Products),
            "GET /customers/lookup.json" => StubResponse.Ok(MaxioPayloads.Customer),
            "GET /customers/555/subscriptions.json" => StubResponse.Ok(MaxioPayloads.NoSubscriptions),
            "POST /subscriptions.json" => StubResponse.Duplicate(),
            _ => throw new InvalidOperationException(path)
        }));

        await Assert.ThrowsAsync<BillingConflictException>(
            () => context.Service.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro")));
    }

    [Fact]
    public async Task Concurrent_subscribes_for_one_shopper_create_a_single_subscription()
    {
        var subscriptionCreated = false;

        var context = new MaxioTestContext((request, _) => Route(request, path => path switch
        {
            "GET /site.json" => StubResponse.Ok(MaxioPayloads.Site),
            "GET /product_families/handle:test-family/products.json" => StubResponse.Ok(MaxioPayloads.Products),
            "GET /customers/lookup.json" => StubResponse.Ok(MaxioPayloads.Customer),
            "GET /customers/555/subscriptions.json" => subscriptionCreated
                ? StubResponse.Ok(MaxioPayloads.ProSubscriptionList)
                : StubResponse.Ok(MaxioPayloads.NoSubscriptions),
            "POST /subscriptions.json" => Mark(() => subscriptionCreated = true, StubResponse.Created(MaxioPayloads.ProSubscription)),
            _ => throw new InvalidOperationException(path)
        }));

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => context.Service.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"))));

        Assert.Equal(1, results.Count(r => r.Created));
        Assert.Equal(1, context.Handler.CountOf("POST", "/subscriptions.json"));
        Assert.All(results, r => Assert.Equal(777, r.Subscription.Id));
    }

    [Fact]
    public async Task Subscribe_sends_a_uniqueness_token_and_the_sites_payment_collection_method()
    {
        var context = new MaxioTestContext((request, _) => Route(request, path => path switch
        {
            "GET /site.json" => StubResponse.Ok(MaxioPayloads.Site),
            "GET /product_families/handle:test-family/products.json" => StubResponse.Ok(MaxioPayloads.Products),
            "GET /customers/lookup.json" => StubResponse.Ok(MaxioPayloads.Customer),
            "GET /customers/555/subscriptions.json" => StubResponse.Ok(MaxioPayloads.NoSubscriptions),
            "POST /subscriptions.json" => StubResponse.Created(MaxioPayloads.ProSubscription),
            _ => throw new InvalidOperationException(path)
        }));

        await context.Service.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        var body = context.Handler.Requests.Single(r => r.PathAndQuery == "/subscriptions.json").Body!;
        Assert.Contains("\"uniqueness_token\"", body);
        Assert.Contains("\"customer_id\":555", body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
    }

    [Fact]
    public async Task Subscribe_uses_invoice_collection_on_a_statement_based_site()
    {
        var context = new MaxioTestContext((request, _) => Route(request, path => path switch
        {
            "GET /site.json" => StubResponse.Ok(MaxioPayloads.StatementBasedSite),
            "GET /product_families/handle:test-family/products.json" => StubResponse.Ok(MaxioPayloads.Products),
            "GET /customers/lookup.json" => StubResponse.Ok(MaxioPayloads.Customer),
            "GET /customers/555/subscriptions.json" => StubResponse.Ok(MaxioPayloads.NoSubscriptions),
            "POST /subscriptions.json" => StubResponse.Created(MaxioPayloads.ProSubscription),
            _ => throw new InvalidOperationException(path)
        }));

        await context.Service.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        var body = context.Handler.Requests.Single(r => r.PathAndQuery == "/subscriptions.json").Body!;
        Assert.Contains("\"payment_collection_method\":\"invoice\"", body);
    }

    [Fact]
    public async Task Subscribe_honours_an_explicitly_configured_payment_collection_method_without_reading_the_site()
    {
        var context = new MaxioTestContext(
            (request, _) => Route(request, path => path switch
            {
                "GET /product_families/handle:test-family/products.json" => StubResponse.Ok(MaxioPayloads.Products),
                "GET /customers/lookup.json" => StubResponse.Ok(MaxioPayloads.Customer),
                "GET /customers/555/subscriptions.json" => StubResponse.Ok(MaxioPayloads.NoSubscriptions),
                "POST /subscriptions.json" => StubResponse.Created(MaxioPayloads.ProSubscription),
                _ => throw new InvalidOperationException(path)
            }),
            settings => settings.PaymentCollectionMethod = "automatic");

        await context.Service.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        Assert.Equal(0, context.Handler.CountOf("GET", "/site.json"));
        var body = context.Handler.Requests.Single(r => r.PathAndQuery == "/subscriptions.json").Body!;
        Assert.Contains("\"payment_collection_method\":\"automatic\"", body);
    }

    [Fact]
    public async Task Subscribe_rejects_a_plan_outside_the_configured_product_family()
    {
        var context = new MaxioTestContext((request, _) => Route(request, path => path switch
        {
            "GET /product_families/handle:test-family/products.json" => StubResponse.Ok(MaxioPayloads.Products),
            _ => throw new InvalidOperationException(path)
        }));

        var exception = await Assert.ThrowsAsync<BillingPlanNotFoundException>(
            () => context.Service.SubscribeAsync(new SubscribeRequest(Demouser, "some-other-product")));

        Assert.Contains("basic-plan", exception.Message);
        Assert.Equal(0, context.Handler.CountOf("POST", "/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_without_a_plan_or_a_configured_default_is_a_bad_request()
    {
        var context = new MaxioTestContext((request, _) => Route(request, path => path switch
        {
            "GET /product_families/handle:test-family/products.json" => StubResponse.Ok(MaxioPayloads.Products),
            _ => throw new InvalidOperationException(path)
        }));

        await Assert.ThrowsAsync<BillingValidationException>(
            () => context.Service.SubscribeAsync(new SubscribeRequest(Demouser, planHandle: null)));
    }

    [Fact]
    public async Task Subscribe_falls_back_to_the_configured_default_plan()
    {
        var context = new MaxioTestContext(
            (request, _) => Route(request, path => path switch
            {
                "GET /site.json" => StubResponse.Ok(MaxioPayloads.Site),
                "GET /product_families/handle:test-family/products.json" => StubResponse.Ok(MaxioPayloads.Products),
                "GET /customers/lookup.json" => StubResponse.Ok(MaxioPayloads.Customer),
                "GET /customers/555/subscriptions.json" => StubResponse.Ok(MaxioPayloads.NoSubscriptions),
                "POST /subscriptions.json" => StubResponse.Created(MaxioPayloads.ProSubscription),
                _ => throw new InvalidOperationException(path)
            }),
            settings => settings.DefaultPlanHandle = "eshop-pro");

        var result = await context.Service.SubscribeAsync(new SubscribeRequest(Demouser, planHandle: null));

        Assert.True(result.Created);
        Assert.Contains("\"product_handle\":\"eshop-pro\"",
            context.Handler.Requests.Single(r => r.PathAndQuery == "/subscriptions.json").Body!);
    }

    [Fact]
    public async Task Subscribe_surfaces_an_upstream_validation_failure_as_a_billing_validation_error()
    {
        var context = new MaxioTestContext((request, _) => Route(request, path => path switch
        {
            "GET /site.json" => StubResponse.Ok(MaxioPayloads.Site),
            "GET /product_families/handle:test-family/products.json" => StubResponse.Ok(MaxioPayloads.Products),
            "GET /customers/lookup.json" => StubResponse.Ok(MaxioPayloads.Customer),
            "GET /customers/555/subscriptions.json" => StubResponse.Ok(MaxioPayloads.NoSubscriptions),
            "POST /subscriptions.json" => StubResponse.Unprocessable("No payment method was on file"),
            _ => throw new InvalidOperationException(path)
        }));

        var exception = await Assert.ThrowsAsync<BillingValidationException>(
            () => context.Service.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro")));

        Assert.Contains("No payment method was on file", exception.Message);
    }

    [Fact]
    public async Task GetSubscriptions_returns_nothing_when_the_shopper_has_no_billing_customer()
    {
        var context = new MaxioTestContext((request, _) => Route(request, path => path switch
        {
            "GET /customers/lookup.json" => StubResponse.NotFound(),
            _ => throw new InvalidOperationException(path)
        }));

        var subscriptions = await context.Service.GetSubscriptionsAsync(Demouser);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task GetSubscriptions_looks_the_customer_up_by_the_reference_derived_from_the_user_name()
    {
        var context = new MaxioTestContext((request, _) => Route(request, path => path switch
        {
            "GET /customers/lookup.json" => StubResponse.Ok(MaxioPayloads.Customer),
            "GET /customers/555/subscriptions.json" => StubResponse.Ok(MaxioPayloads.ProSubscriptionList),
            _ => throw new InvalidOperationException(path)
        }));

        var subscriptions = await context.Service.GetSubscriptionsAsync(
            new SubscriberIdentity("DemoUser@Microsoft.com", "DemoUser@Microsoft.com"));

        Assert.Single(subscriptions);
        Assert.Contains(
            "reference=eshoponweb%3Ademouser%40microsoft.com",
            context.Handler.Requests.Single(r => r.PathAndQuery.StartsWith("/customers/lookup.json")).PathAndQuery);
    }

    [Fact]
    public async Task Missing_configuration_is_reported_as_a_configuration_error()
    {
        var context = new MaxioTestContext(
            (_, _) => throw new InvalidOperationException("no call should be made"),
            settings => settings.ApiKey = string.Empty);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(() => context.Service.GetPlansAsync());

        Assert.Contains("Maxio:ApiKey", exception.Message);
    }

    [Fact]
    public async Task Throttled_calls_are_retried_and_then_reported_as_unavailable()
    {
        var context = new MaxioTestContext((request, _) => Route(request, path => path switch
        {
            "GET /product_families/handle:test-family/products.json" => StubResponse.Throttled(),
            _ => throw new InvalidOperationException(path)
        }));

        await Assert.ThrowsAsync<BillingUnavailableException>(() => context.Service.GetPlansAsync());

        // The initial attempt plus MaxRetryAttempts retries.
        Assert.Equal(3, context.Handler.CountOf("GET", "/product_families"));
    }

    [Fact]
    public async Task A_transient_failure_is_retried_and_then_succeeds()
    {
        var attempts = 0;

        var context = new MaxioTestContext((request, _) => Route(request, path => path switch
        {
            "GET /product_families/handle:test-family/products.json" => ++attempts == 1
                ? new StubResponse(HttpStatusCode.BadGateway, "{}")
                : StubResponse.Ok(MaxioPayloads.Products),
            _ => throw new InvalidOperationException(path)
        }));

        var plans = await context.Service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal(2, context.Handler.CountOf("GET", "/product_families"));
    }

    private static StubResponse Mark(Action sideEffect, StubResponse response)
    {
        sideEffect();
        return response;
    }
}
