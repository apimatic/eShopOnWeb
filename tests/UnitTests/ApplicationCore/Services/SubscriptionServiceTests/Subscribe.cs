using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class Subscribe
{
    private readonly SubscriptionServiceTestContext _ctx = new();

    [Fact]
    public async Task UnknownPlan_ThrowsWithoutClaimingOrCallingProviderWrites()
    {
        var service = _ctx.CreateService();

        await Assert.ThrowsAsync<UnknownSubscriptionPlanException>(() =>
            service.SubscribeAsync(SubscriptionServiceTestContext.Shopper, "not-offered"));

        Assert.Empty(_ctx.Store.Rows);
        await _ctx.Gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task NewShopper_CreatesCustomerAndSubscription_RecordsActiveEnrollment()
    {
        _ctx.Gateway.CreateSubscriptionAsync(77, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => SubscriptionCreationResult.Created(SubscriptionServiceTestContext.Subscription(500, ci.ArgAt<string>(2))));
        var service = _ctx.CreateService();

        var result = await service.SubscribeAsync(SubscriptionServiceTestContext.Shopper, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(500, result.Subscription!.Id);
        var row = _ctx.Store.Rows[_ctx.ClaimKey];
        Assert.Equal(SubscriptionEnrollmentStatus.Active, row.Status);
        Assert.Equal(500, row.BillingSubscriptionId);
        Assert.Equal(result.SubscriptionReference, row.SubscriptionReference);
        await _ctx.Gateway.Received(1).EnsureCustomerAsync(
            Arg.Is<BillingCustomer>(c => c.Reference == SubscriptionService.CustomerReferenceFor("demouser@microsoft.com")
                && c.Email == "demouser@microsoft.com" && c.FirstName == "Demouser" && c.LastName == "Shopper"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CustomerReference_IsDeterministicAndOpaque()
    {
        var reference = SubscriptionService.CustomerReferenceFor(" DemoUser@Microsoft.com ");

        Assert.Equal(SubscriptionService.CustomerReferenceFor("demouser@microsoft.com"), reference);
        Assert.DoesNotContain("@", reference);
        Assert.StartsWith(SubscriptionService.CustomerReferencePrefix, reference);
    }

    [Fact]
    public async Task RepeatRequest_SamePlan_ReturnsExistingWithoutSecondCreate()
    {
        _ctx.Gateway.CreateSubscriptionAsync(77, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => SubscriptionCreationResult.Created(SubscriptionServiceTestContext.Subscription(500, ci.ArgAt<string>(2))));
        _ctx.Gateway.GetSubscriptionAsync(500, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestContext.Subscription(500, "any"));
        var service = _ctx.CreateService();

        await service.SubscribeAsync(SubscriptionServiceTestContext.Shopper, "eshop-pro");
        var second = await service.SubscribeAsync(SubscriptionServiceTestContext.Shopper, "eshop-pro");

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, second.Outcome);
        await _ctx.Gateway.Received(1).CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistingActiveEnrollment_OtherPlan_Conflicts()
    {
        _ctx.Gateway.CreateSubscriptionAsync(77, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => SubscriptionCreationResult.Created(SubscriptionServiceTestContext.Subscription(500, ci.ArgAt<string>(2))));
        _ctx.Gateway.GetSubscriptionAsync(500, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestContext.Subscription(500, "any"));
        var service = _ctx.CreateService();
        await service.SubscribeAsync(SubscriptionServiceTestContext.Shopper, "eshop-pro");

        await Assert.ThrowsAsync<SubscriptionConflictException>(() =>
            service.SubscribeAsync(SubscriptionServiceTestContext.Shopper, "basic-plan"));
    }

    [Fact]
    public async Task InFlightClaim_SecondCallerConflictsWithoutProviderWrites()
    {
        var pending = new SubscriptionEnrollment(SubscriptionService.CustomerReferenceFor("demouser@microsoft.com"),
            SubscriptionServiceTestContext.Family, "demouser@microsoft.com", "eshop-pro", "ref-inflight", _ctx.Clock.GetUtcNow());
        _ctx.Store.Rows[pending.ClaimKey] = pending;
        var service = _ctx.CreateService();

        await Assert.ThrowsAsync<SubscriptionConflictException>(() =>
            service.SubscribeAsync(SubscriptionServiceTestContext.Shopper, "eshop-pro"));

        await _ctx.Gateway.DidNotReceiveWithAnyArgs().EnsureCustomerAsync(default!, default);
        await _ctx.Gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task PendingClaim_FoundAtProvider_IsSettledAndReturned()
    {
        var pending = new SubscriptionEnrollment(SubscriptionService.CustomerReferenceFor("demouser@microsoft.com"),
            SubscriptionServiceTestContext.Family, "demouser@microsoft.com", "eshop-pro", "ref-landed", _ctx.Clock.GetUtcNow());
        _ctx.Store.Rows[pending.ClaimKey] = pending;
        _ctx.Gateway.FindSubscriptionAsync("ref-landed", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestContext.Subscription(600, "ref-landed"));
        var service = _ctx.CreateService();

        var result = await service.SubscribeAsync(SubscriptionServiceTestContext.Shopper, "eshop-pro");

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.Equal(SubscriptionEnrollmentStatus.Active, pending.Status);
        Assert.Equal(600, pending.BillingSubscriptionId);
    }

    [Fact]
    public async Task StalePendingClaim_NotAtProvider_IsRetriedWithSameReference()
    {
        var pending = new SubscriptionEnrollment(SubscriptionService.CustomerReferenceFor("demouser@microsoft.com"),
            SubscriptionServiceTestContext.Family, "demouser@microsoft.com", "eshop-pro", "ref-stale", _ctx.Clock.GetUtcNow());
        _ctx.Store.Rows[pending.ClaimKey] = pending;
        _ctx.Clock.Advance(SubscriptionService.PendingClaimTakeoverAfter + TimeSpan.FromSeconds(1));
        _ctx.Gateway.CreateSubscriptionAsync(77, "eshop-pro", "ref-stale", Arg.Any<CancellationToken>())
            .Returns(SubscriptionCreationResult.Created(SubscriptionServiceTestContext.Subscription(700, "ref-stale")));
        var service = _ctx.CreateService();

        var result = await service.SubscribeAsync(SubscriptionServiceTestContext.Shopper, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal("ref-stale", result.SubscriptionReference);
    }

    [Fact]
    public async Task UnknownOutcome_ReturnsPendingAndKeepsClaim()
    {
        _ctx.Gateway.CreateSubscriptionAsync(77, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionCreationResult.Unknown());
        var service = _ctx.CreateService();

        var result = await service.SubscribeAsync(SubscriptionServiceTestContext.Shopper, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Pending, result.Outcome);
        Assert.Equal(SubscriptionEnrollmentStatus.Pending, _ctx.Store.Rows[_ctx.ClaimKey].Status);
        Assert.Equal(0, _ctx.Store.Releases);
    }

    [Fact]
    public async Task ProviderRefusesSubscription_ReleasesClaim()
    {
        _ctx.Gateway.CreateSubscriptionAsync(77, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new BillingProviderException("refused", BillingFailureKind.Rejected, 422, new[] { "No payment method" }));
        var service = _ctx.CreateService();

        await Assert.ThrowsAsync<BillingProviderException>(() => service.SubscribeAsync(SubscriptionServiceTestContext.Shopper, "eshop-pro"));

        Assert.Empty(_ctx.Store.Rows);
        Assert.Equal(1, _ctx.Store.Releases);
    }

    [Fact]
    public async Task CustomerStepFails_ReleasesClaim()
    {
        _ctx.Gateway.EnsureCustomerAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>())
            .Throws(new BillingProviderException("down", BillingFailureKind.Unavailable));
        var service = _ctx.CreateService();

        await Assert.ThrowsAsync<BillingProviderException>(() => service.SubscribeAsync(SubscriptionServiceTestContext.Shopper, "eshop-pro"));

        Assert.Empty(_ctx.Store.Rows);
        await _ctx.Gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task LiveSubscriptionAtProvider_IsAdoptedInsteadOfCreatingAnother()
    {
        _ctx.Gateway.ListCustomerSubscriptionsAsync(77, Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionServiceTestContext.Subscription(800, "ref-from-before-restart") });
        var service = _ctx.CreateService();

        var result = await service.SubscribeAsync(SubscriptionServiceTestContext.Shopper, "eshop-pro");

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.Equal(800, _ctx.Store.Rows[_ctx.ClaimKey].BillingSubscriptionId);
        Assert.Equal("ref-from-before-restart", _ctx.Store.Rows[_ctx.ClaimKey].SubscriptionReference);
        await _ctx.Gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task EndedSubscriptionAtProvider_DoesNotBlockNewSubscription()
    {
        _ctx.Gateway.ListCustomerSubscriptionsAsync(77, Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionServiceTestContext.Subscription(801, "old", state: "canceled") });
        _ctx.Gateway.CreateSubscriptionAsync(77, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => SubscriptionCreationResult.Created(SubscriptionServiceTestContext.Subscription(802, ci.ArgAt<string>(2))));
        var service = _ctx.CreateService();

        var result = await service.SubscribeAsync(SubscriptionServiceTestContext.Shopper, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(802, result.Subscription!.Id);
    }
}
