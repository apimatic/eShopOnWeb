using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string UserName = "demouser@microsoft.com";

    private readonly FakeMaxioApiClient _maxio = new();

    [Fact]
    public async Task ListPlansReturnsNonArchivedPlansCheapestFirst()
    {
        var service = CreateService();

        var plans = await service.ListPlansAsync();

        Assert.Equal(
            new[] { FakeMaxioApiClient.BasicPlanHandle, FakeMaxioApiClient.ProPlanHandle },
            plans.Select(p => p.Handle));
        Assert.Equal(29m, plans[0].Price);
        Assert.Equal("USD", plans[0].Currency);
    }

    [Fact]
    public async Task ListPlansExcludesArchivedProducts()
    {
        _maxio.Products = new[]
        {
            new MaxioProduct { Id = 1, Handle = "live", Name = "Live", PriceInCents = 100, Interval = 1, IntervalUnit = "month" },
            new MaxioProduct { Id = 2, Handle = "retired", Name = "Retired", PriceInCents = 100, Interval = 1, IntervalUnit = "month", ArchivedAt = DateTimeOffset.UtcNow }
        };

        var plans = await CreateService().ListPlansAsync();

        Assert.Equal("live", Assert.Single(plans).Handle);
    }

    [Fact]
    public async Task ListPlansFailsWithAConfigurationErrorWhenTheProductFamilyIsMissing()
    {
        _maxio.ProductFamily = null;

        await Assert.ThrowsAsync<BillingConfigurationException>(() => CreateService().ListPlansAsync());
    }

    [Fact]
    public async Task SubscribeCreatesTheCustomerAndTheSubscription()
    {
        var service = CreateService();

        var result = await service.SubscribeAsync(new SubscribeRequest(UserName, FakeMaxioApiClient.ProPlanHandle));

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(FakeMaxioApiClient.ProPlanHandle, result.Subscription.PlanHandle);
        Assert.Equal("active", result.Subscription.State);
        Assert.True(result.Subscription.IsLive);
        Assert.NotNull(result.Subscription.NextBillingAt);
        Assert.Equal($"eshoponweb:{UserName}:{FakeMaxioApiClient.ProPlanHandle}", result.Subscription.Reference);
        Assert.Equal(1, _maxio.CustomerCount);
        Assert.Equal(1, _maxio.SubscriptionCount);
    }

    [Fact]
    public async Task SubscribeUsesTheConfiguredDefaultPlanWhenNoneIsRequested()
    {
        var service = CreateService(settings => settings.DefaultPlanHandle = FakeMaxioApiClient.BasicPlanHandle);

        var result = await service.SubscribeAsync(new SubscribeRequest(UserName));

        Assert.Equal(FakeMaxioApiClient.BasicPlanHandle, result.Subscription.PlanHandle);
    }

    [Fact]
    public async Task SubscribeFallsBackToTheOnlyPlanWhenTheFamilyOffersOne()
    {
        _maxio.Products = new[]
        {
            new MaxioProduct { Id = 1, Handle = "only", Name = "Only", PriceInCents = 500, Interval = 1, IntervalUnit = "month" }
        };

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(UserName));

        Assert.Equal("only", result.Subscription.PlanHandle);
    }

    [Fact]
    public async Task SubscribeRefusesToGuessWhenSeveralPlansExistAndNoDefaultIsConfigured()
    {
        var exception = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(new SubscribeRequest(UserName)));

        // The caller is told what it could have asked for.
        Assert.Contains(FakeMaxioApiClient.ProPlanHandle, exception.Message);
        Assert.Contains(FakeMaxioApiClient.BasicPlanHandle, exception.Message);
        Assert.Equal(0, _maxio.SubscriptionCount);
    }

    [Fact]
    public async Task SubscribeRejectsAPlanThatIsNotOnOffer()
    {
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(new SubscribeRequest(UserName, "no-such-plan")));

        Assert.Equal(0, _maxio.CustomerCount);
    }

    [Fact]
    public async Task SubscribingTwiceReturnsTheOriginalSubscription()
    {
        var service = CreateService();
        var request = new SubscribeRequest(UserName, FakeMaxioApiClient.ProPlanHandle);

        var first = await service.SubscribeAsync(request);
        var second = await service.SubscribeAsync(request);

        Assert.False(first.AlreadySubscribed);
        Assert.True(second.AlreadySubscribed);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Equal(1, _maxio.SubscriptionCount);
        Assert.Equal(1, _maxio.CustomerCount);
    }

    [Fact]
    public async Task ConcurrentSubscribesCreateExactlyOneSubscriptionAndOneCustomer()
    {
        // Reproduces a double-clicked subscribe button: the calls overlap inside the check-then-create
        // sequence, which is where a naive implementation enrolls the shopper more than once.
        _maxio.CallLatency = TimeSpan.FromMilliseconds(20);
        var service = CreateService();
        var request = new SubscribeRequest(UserName, FakeMaxioApiClient.ProPlanHandle);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.SubscribeAsync(request)));

        Assert.Equal(1, _maxio.SubscriptionCount);
        Assert.Equal(1, _maxio.CustomerCount);
        Assert.Single(results.Where(r => !r.AlreadySubscribed));
        Assert.Single(results.Select(r => r.Subscription.Id).Distinct());
    }

    [Fact]
    public async Task ASecondInstanceThatHasAlreadySubscribedTheUserIsObservedOnTheNextAttempt()
    {
        var otherInstance = CreateService();
        var request = new SubscribeRequest(UserName, FakeMaxioApiClient.ProPlanHandle);
        var winner = await otherInstance.SubscribeAsync(request);

        // A service with a cold cache and its own lock re-reads Maxio rather than trusting local state.
        var result = await CreateService().SubscribeAsync(request);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(winner.Subscription.Id, result.Subscription.Id);
        Assert.Equal(1, _maxio.SubscriptionCount);
    }

    [Fact]
    public async Task LosingTheCreateRaceAdoptsTheWinnerInsteadOfFailing()
    {
        // The process-local lock cannot serialise two application instances. This is that case: another
        // instance takes the reference after we read Maxio but before our create lands, so Maxio rejects
        // ours with a duplicate-reference 422. The correct answer is the winner, not an error.
        var service = CreateService();
        var request = new SubscribeRequest(UserName, FakeMaxioApiClient.ProPlanHandle);
        var expectedReference = $"eshoponweb:{UserName}:{FakeMaxioApiClient.ProPlanHandle}";
        MaxioSubscription? competing = null;

        _maxio.OnBeforeCreateSubscription = create =>
        {
            _maxio.OnBeforeCreateSubscription = null;
            competing = _maxio.InsertCompetingSubscription(create.CustomerId, create.ProductHandle, create.Reference);
        };

        var result = await service.SubscribeAsync(request);

        Assert.NotNull(competing);
        Assert.True(result.AlreadySubscribed);
        Assert.Equal(competing!.Id, result.Subscription.Id);
        Assert.Equal(expectedReference, result.Subscription.Reference);
        Assert.Equal(1, _maxio.SubscriptionCount);
    }

    [Fact]
    public async Task AVanishedPlanIsReportedAsAMissingPlanRatherThanAnUpstreamFault()
    {
        // The catalog is cached, so a plan can be archived in Maxio between listing and subscribing.
        var service = CreateService();
        await service.ListPlansAsync();
        _maxio.Products = new[]
        {
            new MaxioProduct { Id = 2, Handle = FakeMaxioApiClient.BasicPlanHandle, Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" }
        };

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(new SubscribeRequest(UserName, FakeMaxioApiClient.ProPlanHandle)));

        // The stale snapshot is dropped, so the next caller sees only what actually exists.
        var plans = await service.ListPlansAsync();
        Assert.Equal(FakeMaxioApiClient.BasicPlanHandle, Assert.Single(plans).Handle);
    }

    [Fact]
    public async Task ResubscribingAfterCancellationCreatesANewSubscriptionWithASuffixedReference()
    {
        var service = CreateService();
        var request = new SubscribeRequest(UserName, FakeMaxioApiClient.ProPlanHandle);

        var original = await service.SubscribeAsync(request);
        _maxio.Cancel(original.Subscription.Id);

        var renewed = await service.SubscribeAsync(request);

        Assert.False(renewed.AlreadySubscribed);
        Assert.NotEqual(original.Subscription.Id, renewed.Subscription.Id);
        Assert.Equal(original.Subscription.Reference + ":2", renewed.Subscription.Reference);
        Assert.Equal(2, _maxio.SubscriptionCount);
        Assert.Equal(1, _maxio.CustomerCount);
    }

    [Fact]
    public async Task SubscribingToASecondPlanDoesNotDisturbTheFirst()
    {
        var service = CreateService();

        await service.SubscribeAsync(new SubscribeRequest(UserName, FakeMaxioApiClient.ProPlanHandle));
        var second = await service.SubscribeAsync(new SubscribeRequest(UserName, FakeMaxioApiClient.BasicPlanHandle));

        Assert.False(second.AlreadySubscribed);
        Assert.Equal(2, _maxio.SubscriptionCount);
        Assert.Equal(1, _maxio.CustomerCount);
    }

    [Fact]
    public async Task ListSubscriptionsReturnsNothingForAUserWhoHasNeverSubscribed()
    {
        var subscriptions = await CreateService().ListSubscriptionsAsync(UserName);

        Assert.Empty(subscriptions);
        Assert.Equal(0, _maxio.CustomerCount);
    }

    [Fact]
    public async Task ListSubscriptionsReturnsTheNewestFirstIncludingCancelledOnes()
    {
        var service = CreateService();
        var pro = await service.SubscribeAsync(new SubscribeRequest(UserName, FakeMaxioApiClient.ProPlanHandle));
        _maxio.Cancel(pro.Subscription.Id);
        var basic = await service.SubscribeAsync(new SubscribeRequest(UserName, FakeMaxioApiClient.BasicPlanHandle));

        var subscriptions = await service.ListSubscriptionsAsync(UserName);

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal(basic.Subscription.Id, subscriptions[0].Id);
        Assert.False(subscriptions.Single(s => s.Id == pro.Subscription.Id).IsLive);
    }

    [Fact]
    public async Task ListSubscriptionsIsCaseInsensitiveInTheUserName()
    {
        var service = CreateService();
        await service.SubscribeAsync(new SubscribeRequest(UserName, FakeMaxioApiClient.ProPlanHandle));

        var subscriptions = await service.ListSubscriptionsAsync(UserName.ToUpperInvariant());

        Assert.Single(subscriptions);
    }

    [Fact]
    public async Task SubscribeSendsTheConfiguredPaymentCollectionMethod()
    {
        var service = CreateService(settings => settings.PaymentCollectionMethod = "automatic");

        await service.SubscribeAsync(new SubscribeRequest(UserName, FakeMaxioApiClient.ProPlanHandle));

        Assert.Equal("automatic", _maxio.CreateSubscriptionCalls.Single().PaymentCollectionMethod);
    }

    /// <summary>
    /// Builds a service over the shared fake. Each call gets its own catalog cache and lock, which is
    /// what makes two of them stand in for two application instances sharing one Maxio site.
    /// </summary>
    private MaxioSubscriptionService CreateService(Action<MaxioSettings>? configure = null)
    {
        var settings = new MaxioSettings
        {
            ApiKey = "not-a-real-key",
            Subdomain = "test-site",
            ProductFamilyHandle = FakeMaxioApiClient.ProductFamilyHandle
        };

        configure?.Invoke(settings);

        return new MaxioSubscriptionService(
            _maxio,
            Options.Create(settings),
            new AsyncTtlCache<MaxioPlanCatalog>(settings.CatalogCacheDuration),
            new KeyedAsyncLock(),
            NullLogger<MaxioSubscriptionService>.Instance);
    }
}
