using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Billing;

public class SubscriptionServiceTests
{
    private static readonly BillingUser Demo = new("DemoUser@Microsoft.com", "demouser@microsoft.com");

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly FakeBillingGateway _gateway = new();

    private CatalogContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_dbName).Options);

    /// <summary>One service per "request": own DbContext (as in a scoped web request), shared store and provider.</summary>
    private SubscriptionService NewService(TimeProvider? time = null)
    {
        var ctx = NewContext();
        return new SubscriptionService(_gateway, new BillingClaimStore(ctx, time), Substitute.For<IAppLogger<SubscriptionService>>(),
            time, TimeSpan.Zero);
    }

    [Fact]
    public async Task RejectsPlanNotOfferedWithoutAnyProviderWrite()
    {
        var service = NewService();

        await Assert.ThrowsAsync<PlanNotOfferedException>(() => service.SubscribeAsync(Demo, "no-such-plan", default));

        Assert.Equal(0, _gateway.CreateCustomerCalls);
        Assert.Equal(0, _gateway.CreateSubscriptionCalls);
        Assert.Empty(await NewContext().SubscriptionClaims.ToListAsync());
    }

    [Fact]
    public async Task FailsClosedWhenListingTruncatedAndPlanAbsent()
    {
        _gateway.PlansTruncated = true;

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => NewService().SubscribeAsync(Demo, "no-such-plan", default));

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
        Assert.Equal(0, _gateway.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscriptionOnce()
    {
        var result = await NewService().SubscribeAsync(Demo, "eshop-pro", default);

        Assert.True(result.Created);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(1, _gateway.CreateCustomerCalls);
        Assert.Equal(1, _gateway.CreateSubscriptionCalls);
        Assert.True(_gateway.Customers.ContainsKey("eshop-user:demouser@microsoft.com"));
        Assert.True(_gateway.Subscriptions.ContainsKey("eshop-sub:demouser@microsoft.com:eshop-pro"));
    }

    [Fact]
    public async Task DefaultsToTheStandardPlanWhenNoneGiven()
    {
        var result = await NewService().SubscribeAsync(Demo, null, default);

        Assert.Equal(SubscriptionService.DefaultPlanHandle, result.Subscription.PlanHandle);
    }

    [Fact]
    public async Task SecondSubscribeReturnsSameSubscriptionWithoutAnotherWrite()
    {
        var first = await NewService().SubscribeAsync(Demo, "eshop-pro", default);
        var second = await NewService().SubscribeAsync(Demo, "eshop-pro", default);

        Assert.False(second.Created);
        Assert.Equal(first.Subscription.SubscriptionId, second.Subscription.SubscriptionId);
        Assert.Equal(1, _gateway.CreateSubscriptionCalls);
        Assert.Equal(1, _gateway.CreateCustomerCalls);
    }

    [Fact]
    public async Task SecondPlanReusesTheExistingCustomer()
    {
        await NewService().SubscribeAsync(Demo, "eshop-pro", default);
        await NewService().SubscribeAsync(Demo, "basic-plan", default);

        Assert.Equal(1, _gateway.CreateCustomerCalls);
        Assert.Equal(2, _gateway.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task ConcurrentDoubleSubscribeReachesTheProviderOnce()
    {
        var release = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        _gateway.CreateSubscriptionGate = () =>
        {
            entered.TrySetResult();
            return release.Task;
        };

        var first = NewService().SubscribeAsync(Demo, "eshop-pro", default);
        await entered.Task; // first request holds the claim and is inside the provider call

        // the double-click: refused by the claim before any provider write
        await Assert.ThrowsAsync<SubscriptionInProgressException>(() => NewService().SubscribeAsync(Demo, "eshop-pro", default));

        release.SetResult();
        var firstResult = await first;
        var third = await NewService().SubscribeAsync(Demo, "eshop-pro", default);

        Assert.True(firstResult.Created);
        Assert.False(third.Created);
        Assert.Equal(firstResult.Subscription.SubscriptionId, third.Subscription.SubscriptionId);
        Assert.Equal(1, _gateway.CreateSubscriptionCalls);
        Assert.Equal(1, _gateway.CreateCustomerCalls);
    }

    [Fact]
    public async Task ProviderRefusalReleasesTheClaimSoTheUserCanRetry()
    {
        _gateway.CreateSubscriptionFailure = new BillingProviderRejectedException("422");
        await Assert.ThrowsAsync<BillingProviderRejectedException>(() => NewService().SubscribeAsync(Demo, "eshop-pro", default));
        Assert.Empty(await NewContext().SubscriptionClaims.ToListAsync());

        _gateway.CreateSubscriptionFailure = null;
        var retry = await NewService().SubscribeAsync(Demo, "eshop-pro", default);

        Assert.True(retry.Created);
    }

    [Fact]
    public async Task UnsettledWriteKeepsClaimAsUnknownAndNextRequestReconcilesByReference()
    {
        _gateway.CreateSubscriptionFailure = new BillingProviderException(BillingFailureKind.OutcomeUnknown, "unknown");
        _gateway.CreateSubscriptionLandsAnyway = true; // the write did reach the provider

        await Assert.ThrowsAsync<BillingProviderException>(() => NewService().SubscribeAsync(Demo, "eshop-pro", default));
        var claim = await NewContext().SubscriptionClaims.SingleAsync();
        Assert.Equal(ClaimState.Unknown, claim.State);

        _gateway.CreateSubscriptionFailure = null;
        var again = await NewService().SubscribeAsync(Demo, "eshop-pro", default);

        Assert.False(again.Created); // found by reference, no second write
        Assert.Equal(1, _gateway.CreateSubscriptionCalls);
        Assert.Equal(ClaimState.Completed, (await NewContext().SubscriptionClaims.SingleAsync()).State);
    }

    [Fact]
    public async Task UnknownClaimWithNothingAtTheProviderIsTakenOverAndRetried()
    {
        _gateway.CreateSubscriptionFailure = new BillingProviderException(BillingFailureKind.OutcomeUnknown, "unknown");
        await Assert.ThrowsAsync<BillingProviderException>(() => NewService().SubscribeAsync(Demo, "eshop-pro", default));

        _gateway.CreateSubscriptionFailure = null;
        var retry = await NewService().SubscribeAsync(Demo, "eshop-pro", default);

        Assert.True(retry.Created);
        Assert.Equal(2, _gateway.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task MySubscriptionsIsEmptyForAUserWithoutACustomer()
    {
        var list = await NewService().ListMySubscriptionsAsync(Demo, default);

        Assert.Empty(list);
    }

    [Fact]
    public async Task MySubscriptionsListsTheCallersSubscriptions()
    {
        await NewService().SubscribeAsync(Demo, "eshop-pro", default);

        var list = await NewService().ListMySubscriptionsAsync(Demo, default);

        var sub = Assert.Single(list);
        Assert.Equal("eshop-pro", sub.PlanHandle);
        Assert.NotNull(sub.NextBillingDate);
    }

    [Fact]
    public async Task RequiresAnEmailForANewCustomer()
    {
        var noEmail = new BillingUser("someone", null);

        await Assert.ThrowsAsync<BillingRequestException>(() => NewService().SubscribeAsync(noEmail, "eshop-pro", default));

        Assert.Empty(await NewContext().SubscriptionClaims.ToListAsync());
        Assert.Equal(0, _gateway.CreateSubscriptionCalls);
    }
}
