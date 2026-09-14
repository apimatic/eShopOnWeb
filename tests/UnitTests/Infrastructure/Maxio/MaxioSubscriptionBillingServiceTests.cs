using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private const string ProductFamilyHandle = "eshop-subscribe";

    private readonly FakeMaxioApi _api = new();
    private readonly SubscriberAccount _account = new("demouser@microsoft.com", "demouser@microsoft.com");

    public MaxioSubscriptionBillingServiceTests()
    {
        _api.Products.Add(FakeMaxioApi.Product("eshop-pro", "Pro Plan", 29900));
        _api.Products.Add(FakeMaxioApi.Product("basic-plan", "Basic Plan", 2900));
    }

    private MaxioSubscriptionBillingService CreateService() => new(
        _api,
        new MaxioSiteCache(),
        new KeyedAsyncLock(),
        Options.Create(new MaxioSettings
        {
            ApiKey = "not-a-real-key",
            Subdomain = "test",
            ProductFamilyHandle = ProductFamilyHandle
        }),
        NullLogger<MaxioSubscriptionBillingService>.Instance);

    [Fact]
    public async Task ListPlansAsync_ReturnsActivePlansOrderedByPrice()
    {
        _api.Products.Add(FakeMaxioApi.Product("retired-plan", "Retired Plan", 100, archivedAt: DateTimeOffset.UtcNow));

        var plans = await CreateService().ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(plan => plan.Handle));
        Assert.Equal(299m, plans.Single(plan => plan.Handle == "eshop-pro").Price);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription()
    {
        var result = await CreateService().SubscribeAsync(_account, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal("active", result.Subscription.State);
        Assert.True(result.Subscription.IsLive);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(299m, result.Subscription.Price);
        Assert.NotNull(result.Subscription.NextBillingAt);
        Assert.Equal(1, _api.CreateCustomerCalls);
        Assert.Equal(1, _api.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task SubscribeAsync_NamesTheCustomerReferenceAfterTheAccountKey()
    {
        await CreateService().SubscribeAsync(_account, "eshop-pro");

        var customer = await _api.FindCustomerByReferenceAsync("eshoponweb:demouser@microsoft.com");

        Assert.NotNull(customer);
        Assert.Equal("demouser@microsoft.com", customer!.Email);
    }

    [Fact]
    public async Task SubscribeAsync_ReusesAnExistingCustomer()
    {
        _api.SeedCustomer("eshoponweb:demouser@microsoft.com", "demouser@microsoft.com");

        await CreateService().SubscribeAsync(_account, "eshop-pro");

        Assert.Equal(0, _api.CreateCustomerCalls);
    }

    [Fact]
    public async Task SubscribeAsync_RepeatedRequestReturnsTheExistingSubscription()
    {
        var service = CreateService();

        var first = await service.SubscribeAsync(_account, "eshop-pro");
        var second = await service.SubscribeAsync(_account, "eshop-pro");

        Assert.False(first.AlreadyExisted);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Equal(1, _api.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task SubscribeAsync_SurvivesARestartBecauseTheBillingSystemIsTheSystemOfRecord()
    {
        var first = await CreateService().SubscribeAsync(_account, "eshop-pro");

        // A brand new service instance holds no local state at all - only the billing system does.
        var second = await CreateService().SubscribeAsync(_account, "eshop-pro");

        Assert.True(second.AlreadyExisted);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeAsync_ConcurrentRequestsCreateASingleSubscription()
    {
        _api.Latency = TimeSpan.FromMilliseconds(20);
        var service = CreateService();

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => service.SubscribeAsync(_account, "eshop-pro")));

        Assert.Equal(1, _api.CreateCustomerCalls);
        Assert.Equal(1, _api.CreateSubscriptionCalls);
        Assert.Single(results.Where(result => !result.AlreadyExisted));
        Assert.Single(results.Select(result => result.Subscription.Id).Distinct());
    }

    [Fact]
    public async Task SubscribeAsync_ConcurrentRequestsAcrossInstancesCreateASingleSubscription()
    {
        // Separate instances share no in-process lock, so only the server side unique reference and
        // uniqueness token stand between the callers and a duplicate.
        _api.Latency = TimeSpan.FromMilliseconds(20);

        var results = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => CreateService().SubscribeAsync(_account, "eshop-pro")));

        Assert.Single(results.Select(result => result.Subscription.Id).Distinct());
        Assert.Single(results.Where(result => !result.AlreadyExisted));
    }

    [Fact]
    public async Task SubscribeAsync_ReconcilesADuplicateSubmissionToTheSubscriptionThatWasAccepted()
    {
        // The server accepted the create but the caller never saw the response, so the replay comes
        // back as a duplicate submission.
        _api.LoseNextCreateResponse = true;

        var result = await CreateService().SubscribeAsync(_account, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal("eshoponweb:demouser@microsoft.com:eshop-pro:0", result.Subscription.Reference);
        Assert.Equal("active", result.Subscription.State);
    }

    [Fact]
    public async Task SubscribeAsync_ReportsADuplicateThatCannotBeReconciled()
    {
        _api.RejectNextCreateAsDuplicate = true;

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => CreateService().SubscribeAsync(_account, "eshop-pro"));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubscribeAsync_UsesAFreshUniquenessTokenPerAttemptSoAPurgedRecordDoesNotBlockSignup()
    {
        var service = CreateService();
        await service.SubscribeAsync(_account, "eshop-pro");

        // The billing site is re-seeded, or the subscription is purged, and the shopper subscribes
        // again. A uniqueness token derived from the request would still be inside the server's
        // de-duplication window and would reject this.
        var replacement = new FakeMaxioApi();
        replacement.Products.AddRange(_api.Products);

        var result = await new MaxioSubscriptionBillingService(
            replacement,
            new MaxioSiteCache(),
            new KeyedAsyncLock(),
            Options.Create(new MaxioSettings { ApiKey = "not-a-real-key", Subdomain = "test", ProductFamilyHandle = ProductFamilyHandle }),
            NullLogger<MaxioSubscriptionBillingService>.Instance)
            .SubscribeAsync(_account, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal("active", result.Subscription.State);
    }

    [Fact]
    public async Task SubscribeAsync_IgnoresEndOfLifeSubscriptionsAndSubscribesAgain()
    {
        var customer = _api.SeedCustomer("eshoponweb:demouser@microsoft.com", "demouser@microsoft.com");
        var product = _api.Products.Single(candidate => candidate.Handle == "eshop-pro");
        _api.Seed(customer, product, "canceled", "eshoponweb:demouser@microsoft.com:eshop-pro:0");

        var result = await CreateService().SubscribeAsync(_account, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal("active", result.Subscription.State);

        // The ordinal moves on so the new subscription does not collide with the canceled one.
        Assert.Equal("eshoponweb:demouser@microsoft.com:eshop-pro:1", result.Subscription.Reference);
    }

    [Fact]
    public async Task SubscribeAsync_TreatsAPastDueSubscriptionAsStillSubscribed()
    {
        var customer = _api.SeedCustomer("eshoponweb:demouser@microsoft.com", "demouser@microsoft.com");
        var product = _api.Products.Single(candidate => candidate.Handle == "eshop-pro");
        var seeded = _api.Seed(customer, product, "past_due", "eshoponweb:demouser@microsoft.com:eshop-pro:0");

        var result = await CreateService().SubscribeAsync(_account, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(seeded.Id, result.Subscription.Id);
        Assert.Equal(0, _api.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task SubscribeAsync_SubscribesToADifferentPlanSeparately()
    {
        var service = CreateService();

        var pro = await service.SubscribeAsync(_account, "eshop-pro");
        var basic = await service.SubscribeAsync(_account, "basic-plan");

        Assert.NotEqual(pro.Subscription.Id, basic.Subscription.Id);
        Assert.Equal(1, _api.CreateCustomerCalls);
        Assert.Equal(2, _api.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task SubscribeAsync_MatchesThePlanHandleCaseInsensitively()
    {
        var result = await CreateService().SubscribeAsync(_account, "ESHOP-Pro");

        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsWhenThePlanIsUnknown()
    {
        var exception = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(_account, "no-such-plan"));

        Assert.Equal("no-such-plan", exception.PlanHandle);
        Assert.Equal(ProductFamilyHandle, exception.ProductFamilyHandle);
        Assert.Equal(0, _api.CreateCustomerCalls);
    }

    [Fact]
    public async Task SubscribeAsync_RefusesAPlanThatRequiresAPaymentMethod()
    {
        _api.Products.Add(FakeMaxioApi.Product("card-plan", "Card Plan", 500, requireCreditCard: true));

        await Assert.ThrowsAsync<SubscriptionBillingValidationException>(
            () => CreateService().SubscribeAsync(_account, "card-plan"));

        Assert.Equal(0, _api.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task SubscribeAsync_UsesInvoiceCollectionOnStatementBasedSites()
    {
        _api.Site = new MaxioSite { RelationshipInvoicingEnabled = false };

        var result = await CreateService().SubscribeAsync(_account, "eshop-pro");

        Assert.Equal("invoice", result.Subscription.PaymentCollectionMethod);
    }

    [Fact]
    public async Task SubscribeAsync_UsesRemittanceCollectionOnRelationshipInvoicingSites()
    {
        var result = await CreateService().SubscribeAsync(_account, "eshop-pro");

        Assert.Equal("remittance", result.Subscription.PaymentCollectionMethod);
    }

    [Fact]
    public async Task SubscribeAsync_ReadsTheSiteConfigurationOnlyOnce()
    {
        var service = CreateService();

        await service.SubscribeAsync(_account, "eshop-pro");
        await service.SubscribeAsync(_account, "basic-plan");

        Assert.Equal(1, _api.ReadSiteCalls);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsEmptyWhenTheShopperHasNeverSubscribed()
    {
        var subscriptions = await CreateService().ListSubscriptionsAsync(_account);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsTheShoppersSubscriptionsNewestFirst()
    {
        var service = CreateService();
        await service.SubscribeAsync(_account, "eshop-pro");
        await service.SubscribeAsync(_account, "basic-plan");

        var subscriptions = await service.ListSubscriptionsAsync(_account);

        Assert.Equal(2, subscriptions.Count);
        Assert.True(subscriptions[0].CreatedAt >= subscriptions[1].CreatedAt);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_DoesNotReturnAnotherShoppersSubscriptions()
    {
        var service = CreateService();
        await service.SubscribeAsync(_account, "eshop-pro");
        await service.SubscribeAsync(new SubscriberAccount("someone.else@example.com", "someone.else@example.com"), "basic-plan");

        var subscriptions = await service.ListSubscriptionsAsync(_account);

        Assert.Equal("eshop-pro", Assert.Single(subscriptions).PlanHandle);
    }
}
