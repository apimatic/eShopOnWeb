using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly SubscriberIdentity Demouser =
        new("demouser@microsoft.com", "demouser@microsoft.com");

    private const string CustomerReference = "eshoponweb-demouser@microsoft.com";

    private static ScriptedHttpMessageHandler Handler() => new ScriptedHttpMessageHandler()
        .On(HttpMethod.Get, "/site.json", HttpStatusCode.OK, MaxioPayloads.Site)
        .On(HttpMethod.Get, "/products.json", HttpStatusCode.OK, MaxioPayloads.ProductFamilyProducts);

    [Fact]
    public async Task GetPlansAsync_ReturnsLivePlansCheapestFirstWithSiteCurrency()
    {
        using var host = new MaxioTestHost(Handler());

        var plans = await host.BillingService.GetPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(p => p.Handle));
        Assert.All(plans, p => Assert.Equal("USD", p.Currency));

        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentMethod);
    }

    [Fact]
    public async Task GetPlansAsync_ExcludesArchivedPlans()
    {
        using var host = new MaxioTestHost(Handler());

        var plans = await host.BillingService.GetPlansAsync();

        Assert.DoesNotContain(plans, p => p.Handle == "retired-plan");
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerThenSubscription_WhenUserIsUnknown()
    {
        var handler = Handler()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, "")
            .On(HttpMethod.Post, "/customers.json", HttpStatusCode.Created, MaxioPayloads.Customer)
            .On(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioPayloads.NoSubscriptions)
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, MaxioPayloads.CreatedSubscription());

        using var host = new MaxioTestHost(handler);

        var result = await host.BillingService.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(94208329, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.True(result.Subscription.IsLive);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal(CustomerReference, result.Subscription.CustomerReference);
        Assert.NotNull(result.Subscription.NextBillingAt);

        var created = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Path.Contains("/customers.json"));
        Assert.Contains($"\"reference\":\"{CustomerReference}\"", created.Body);
    }

    [Fact]
    public async Task SubscribeAsync_SendsUniquenessTokenAlongsideTheSubscription_NotInsideIt()
    {
        var handler = Handler()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioPayloads.Customer)
            .On(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioPayloads.NoSubscriptions)
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, MaxioPayloads.CreatedSubscription());

        using var host = new MaxioTestHost(handler);
        await host.BillingService.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        var body = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Path.Contains("/subscriptions.json")).Body!;

        // Maxio only honours uniqueness_token as a sibling of "subscription"; nested it is ignored.
        using var document = System.Text.Json.JsonDocument.Parse(body);
        Assert.True(document.RootElement.TryGetProperty("uniqueness_token", out var token));
        Assert.False(string.IsNullOrWhiteSpace(token.GetString()));
        Assert.False(document.RootElement.GetProperty("subscription").TryGetProperty("uniqueness_token", out _));
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingSubscription_WithoutCreatingASecondOne()
    {
        var existing = MaxioPayloads.SubscriptionList(
            MaxioPayloads.SubscriptionBody(94208329, "active", "eshop-pro", 29900));

        var handler = Handler()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioPayloads.Customer)
            .On(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, existing);

        using var host = new MaxioTestHost(handler);

        var result = await host.BillingService.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(94208329, result.Subscription.Id);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task SubscribeAsync_IgnoresCanceledSubscriptionsAndSubscribesAgain()
    {
        var canceled = MaxioPayloads.SubscriptionList(
            MaxioPayloads.SubscriptionBody(94208000, "canceled", "eshop-pro", 29900));

        var handler = Handler()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioPayloads.Customer)
            .On(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, canceled)
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, MaxioPayloads.CreatedSubscription());

        using var host = new MaxioTestHost(handler);

        var result = await host.BillingService.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(94208329, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeAsync_DoesNotConfuseADifferentPlanForThisOne()
    {
        var otherPlan = MaxioPayloads.SubscriptionList(
            MaxioPayloads.SubscriptionBody(94208330, "active", "basic-plan", 2900));

        var handler = Handler()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioPayloads.Customer)
            .On(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, otherPlan)
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, MaxioPayloads.CreatedSubscription());

        using var host = new MaxioTestHost(handler);

        var result = await host.BillingService.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        Assert.False(result.AlreadySubscribed);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
    }

    [Fact]
    public async Task SubscribeAsync_ReusesTheCustomerWhenTwoWritersRaceOnTheSameReference()
    {
        var handler = Handler()
            // The first lookup misses, so a create is attempted; by then the twin request has won.
            .OnSequence(HttpMethod.Get, "/customers/lookup.json",
                (HttpStatusCode.NotFound, ""),
                (HttpStatusCode.OK, MaxioPayloads.Customer))
            .On(HttpMethod.Post, "/customers.json", HttpStatusCode.UnprocessableEntity, MaxioPayloads.DuplicateReferenceError)
            .On(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioPayloads.NoSubscriptions)
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, MaxioPayloads.CreatedSubscription());

        using var host = new MaxioTestHost(handler);

        var result = await host.BillingService.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        Assert.Equal(98837189, result.Subscription.CustomerId);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task SubscribeAsync_ResolvesADuplicateSubmissionToTheSubscriptionTheTwinCreated()
    {
        var created = MaxioPayloads.SubscriptionList(
            MaxioPayloads.SubscriptionBody(94208329, "active", "eshop-pro", 29900));

        var handler = Handler()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioPayloads.Customer)
            // Nothing yet on the first read; by the time the 409 comes back the twin has committed.
            .OnSequence(HttpMethod.Get, "/subscriptions.json",
                (HttpStatusCode.OK, MaxioPayloads.NoSubscriptions),
                (HttpStatusCode.OK, created))
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Conflict, MaxioPayloads.DuplicateSubmissionError);

        using var host = new MaxioTestHost(handler);

        var result = await host.BillingService.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(94208329, result.Subscription.Id);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_RetriesWithAFreshTokenWhenADuplicateResolvesToNothing()
    {
        // Re-subscribing after a cancellation inside the 60 minute duplicate-prevention window:
        // the deterministic token collides, but there is no live subscription to hand back.
        var handler = Handler()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioPayloads.Customer)
            .On(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioPayloads.NoSubscriptions)
            .OnSequence(HttpMethod.Post, "/subscriptions.json",
                (HttpStatusCode.Conflict, MaxioPayloads.DuplicateSubmissionError),
                (HttpStatusCode.Created, MaxioPayloads.CreatedSubscription()));

        using var host = new MaxioTestHost(handler);

        var result = await host.BillingService.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(2, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));

        var posts = handler.Requests.Where(r => r.Method == HttpMethod.Post && r.Path.Contains("/subscriptions.json")).ToList();
        Assert.NotEqual(posts[0].Body, posts[1].Body);
    }

    [Fact]
    public async Task SubscribeAsync_RejectsAPlanOutsideTheConfiguredFamily()
    {
        using var host = new MaxioTestHost(Handler());

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => host.BillingService.SubscribeAsync(new SubscribeRequest(Demouser, "some-other-product")));

        Assert.Equal(404, exception.StatusCode);
        Assert.Equal(0, host.Handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_SurfacesAProviderRejectionAsAClientError()
    {
        var handler = Handler()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioPayloads.Customer)
            .On(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioPayloads.NoSubscriptions)
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.UnprocessableEntity, MaxioPayloads.NoPaymentMethodError);

        using var host = new MaxioTestHost(handler);

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => host.BillingService.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro")));

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("No payment method", exception.Message);
    }

    [Fact]
    public async Task SubscribeAsync_ReportsProviderOutageAsServiceUnavailable()
    {
        var handler = Handler()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioPayloads.Customer)
            .On(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioPayloads.NoSubscriptions)
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.BadGateway, "");

        using var host = new MaxioTestHost(handler);

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => host.BillingService.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro")));

        Assert.Equal(503, exception.StatusCode);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsEmpty_WhenTheUserHasNoBillingCustomerYet()
    {
        var handler = Handler().On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, "");

        using var host = new MaxioTestHost(handler);

        var subscriptions = await host.BillingService.GetSubscriptionsAsync(Demouser);

        Assert.Empty(subscriptions);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsNewestFirstAndDropsTheNextBillingDateOnDeadSubscriptions()
    {
        var subscriptions = MaxioPayloads.SubscriptionList(
            MaxioPayloads.SubscriptionBody(1, "canceled", "basic-plan", 2900, "2026-01-01T00:00:00+00:00"),
            MaxioPayloads.SubscriptionBody(2, "active", "eshop-pro", 29900, "2026-09-06T10:16:05+05:00"));

        var handler = Handler()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioPayloads.Customer)
            .On(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, subscriptions);

        using var host = new MaxioTestHost(handler);

        var result = await host.BillingService.GetSubscriptionsAsync(Demouser);

        Assert.Equal(new long[] { 2, 1 }, result.Select(s => s.Id));
        Assert.True(result[0].IsLive);
        Assert.NotNull(result[0].NextBillingAt);
        Assert.False(result[1].IsLive);
        Assert.Null(result[1].NextBillingAt);
    }

    [Fact]
    public async Task SubscribeAsync_UsesAnInvoiceBasedCollectionMethodSoNoCardIsNeeded()
    {
        var handler = Handler()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioPayloads.Customer)
            .On(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioPayloads.NoSubscriptions)
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, MaxioPayloads.CreatedSubscription());

        using var host = new MaxioTestHost(handler);
        await host.BillingService.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        var body = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Path.Contains("/subscriptions.json")).Body!;
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
    }

    [Fact]
    public async Task SubscribeAsync_HonoursAnExplicitPaymentCollectionMethodSetting()
    {
        var handler = Handler()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioPayloads.Customer)
            .On(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioPayloads.NoSubscriptions)
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, MaxioPayloads.CreatedSubscription());

        using var host = new MaxioTestHost(handler, new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-key",
            ["Maxio:Subdomain"] = "test-site",
            ["Maxio:ProductFamilyHandle"] = "demo-plans",
            ["Maxio:PaymentCollectionMethod"] = "invoice",
            ["Maxio:MaxRetryAttempts"] = "0"
        });

        await host.BillingService.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        var body = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Path.Contains("/subscriptions.json")).Body!;
        Assert.Contains("\"payment_collection_method\":\"invoice\"", body);
    }

    [Fact]
    public async Task SubscribeAsync_AddressesTheConfiguredProductFamilyByHandle()
    {
        var handler = Handler()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioPayloads.Customer)
            .On(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioPayloads.NoSubscriptions)
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, MaxioPayloads.CreatedSubscription());

        using var host = new MaxioTestHost(handler);
        await host.BillingService.SubscribeAsync(new SubscribeRequest(Demouser, "eshop-pro"));

        // Handles are stable across re-seeds; numeric ids are not, so the family is never addressed by id.
        Assert.Contains(handler.Requests, r => r.Path.Contains("/product_families/handle:demo-plans/products.json"));
    }
}
