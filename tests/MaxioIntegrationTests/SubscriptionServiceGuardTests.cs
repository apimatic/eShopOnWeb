using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Fast, deterministic tests of <see cref="SubscriptionService"/>'s own logic — state-machine
/// guards, ownership checks, and the plan-change stale-preview rejection — against a fake
/// <see cref="IBillingClient"/>. These don't hit the network; they prove the service actually
/// stops an illegal call before it would ever reach the provider (asserted via
/// <c>DidNotReceive()</c>), which the live sandbox tests elsewhere in this project can't cheaply
/// exercise (they can't force a price to drift mid-test).
/// </summary>
public class SubscriptionServiceGuardTests
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private SubscriptionService CreateSut() => new(_billingClient, _publisher, _logger);

    private static BillingSubscription Subscription(int id, SubscriptionLifecycleState state, string productHandle = "eshop-pro", string? customerReference = "owner@example.com") =>
        new(id, 1, customerReference, 7126957, productHandle, 29900, state, null, null, false, null, null);

    [Fact]
    public async Task RecordUsageAsync_ZeroQuantity_ThrowsWithoutCallingProvider()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.RecordUsageAsync(1, 0m, null, "owner@example.com"));

        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordUsageAsync_NegativeQuantity_ThrowsWithoutCallingProvider()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.RecordUsageAsync(1, -5m, null, "owner@example.com"));

        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordUsageAsync_OnPausedSubscription_ThrowsInvalidTransitionWithoutCallingProvider()
    {
        _billingClient.GetSubscriptionAsync(42, Arg.Any<CancellationToken>()).Returns(Subscription(42, SubscriptionLifecycleState.Paused));
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => sut.RecordUsageAsync(42, 1m, null, "owner@example.com"));

        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PauseAsync_ForAnotherUsersSubscription_ThrowsAccessDeniedWithoutCallingProvider()
    {
        _billingClient.GetSubscriptionAsync(7, Arg.Any<CancellationToken>()).Returns(Subscription(7, SubscriptionLifecycleState.Active, customerReference: "owner@example.com"));
        var sut = CreateSut();

        await Assert.ThrowsAsync<SubscriptionAccessDeniedException>(
            () => sut.PauseAsync(7, "someone-else@example.com"));

        await _billingClient.DidNotReceive().PauseSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PauseAsync_AsAdmin_BypassesOwnershipCheck()
    {
        _billingClient.GetSubscriptionAsync(7, Arg.Any<CancellationToken>()).Returns(Subscription(7, SubscriptionLifecycleState.Active, customerReference: "owner@example.com"));
        _billingClient.PauseSubscriptionAsync(7, Arg.Any<CancellationToken>()).Returns(Subscription(7, SubscriptionLifecycleState.Paused, customerReference: "owner@example.com"));
        var sut = CreateSut();

        var result = await sut.PauseAsync(7, ownerReference: null);

        Assert.Equal(SubscriptionLifecycleState.Paused, result.State);
        await _billingClient.Received(1).PauseSubscriptionAsync(7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_OnActiveSubscription_ThrowsInvalidTransitionWithoutCallingProvider()
    {
        _billingClient.GetSubscriptionAsync(3, Arg.Any<CancellationToken>()).Returns(Subscription(3, SubscriptionLifecycleState.Active));
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => sut.ResumeAsync(3, "owner@example.com"));

        await _billingClient.DidNotReceive().ResumeSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelAsync_OnAlreadyCanceledSubscription_ThrowsInvalidTransitionWithoutCallingProvider()
    {
        _billingClient.GetSubscriptionAsync(9, Arg.Any<CancellationToken>()).Returns(Subscription(9, SubscriptionLifecycleState.Canceled));
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => sut.CancelAsync(9, endOfPeriod: false, reason: null, ownerReference: "owner@example.com"));

        await _billingClient.DidNotReceive().CancelSubscriptionAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReactivateAsync_OnActiveSubscription_ThrowsInvalidTransitionWithoutCallingProvider()
    {
        _billingClient.GetSubscriptionAsync(11, Arg.Any<CancellationToken>()).Returns(Subscription(11, SubscriptionLifecycleState.Active));
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => sut.ReactivateAsync(11, "owner@example.com"));

        await _billingClient.DidNotReceive().ReactivateSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_SamePlanAsCurrent_ThrowsInvalidTransitionWithoutCallingProvider()
    {
        _billingClient.GetSubscriptionAsync(5, Arg.Any<CancellationToken>()).Returns(Subscription(5, SubscriptionLifecycleState.Active, productHandle: "eshop-pro"));
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => sut.PreviewPlanChangeAsync(5, "eshop-pro", applyNow: true, ownerReference: "owner@example.com"));

        await _billingClient.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitPlanChangeAsync_WhenFreshPreviewDiffersFromShown_ThrowsStalePreviewWithoutCommitting()
    {
        var subscription = Subscription(6, SubscriptionLifecycleState.Active, productHandle: "eshop-pro");
        _billingClient.GetSubscriptionAsync(6, Arg.Any<CancellationToken>()).Returns(subscription);

        var shownPreview = new PlanChangePreview(true, 1000, 2000, 2000, null, 2900, null, null);
        var freshPreviewWithDifferentAmount = new PlanChangePreview(true, 1500, 2000, 2000, null, 2900, null, null);
        _billingClient.PreviewPlanChangeAsync(6, "basic-plan", true, Arg.Any<CancellationToken>()).Returns(freshPreviewWithDifferentAmount);

        var sut = CreateSut();

        await Assert.ThrowsAsync<PlanChangePreviewStaleException>(
            () => sut.CommitPlanChangeAsync(6, "basic-plan", applyNow: true, shownPreview, ownerReference: "owner@example.com"));

        await _billingClient.DidNotReceive().CommitPlanChangeNowAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitPlanChangeAsync_WhenFreshPreviewMatchesShown_CommitsSuccessfully()
    {
        var subscription = Subscription(6, SubscriptionLifecycleState.Active, productHandle: "eshop-pro");
        _billingClient.GetSubscriptionAsync(6, Arg.Any<CancellationToken>()).Returns(subscription);

        var shownPreview = new PlanChangePreview(true, 1000, 2000, 2000, null, 2900, null, null);
        _billingClient.PreviewPlanChangeAsync(6, "basic-plan", true, Arg.Any<CancellationToken>()).Returns(shownPreview);
        _billingClient.CommitPlanChangeNowAsync(6, "basic-plan", Arg.Any<CancellationToken>())
            .Returns(Subscription(6, SubscriptionLifecycleState.Active, productHandle: "basic-plan"));

        var sut = CreateSut();

        var updated = await sut.CommitPlanChangeAsync(6, "basic-plan", applyNow: true, shownPreview, ownerReference: "owner@example.com");

        Assert.Equal("basic-plan", updated.ProductHandle);
        await _billingClient.Received(1).CommitPlanChangeNowAsync(6, "basic-plan", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_WhenLiveSubscriptionAlreadyExists_ReturnsExistingWithoutCreatingDuplicate()
    {
        var existing = Subscription(20, SubscriptionLifecycleState.Active, customerReference: "owner@example.com");
        _billingClient.GetPlanByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(new BillingPlan(7126957, "eshop-pro", "Pro Plan", 29900, "month", 1));
        _billingClient.EnsureCustomerAsync("owner@example.com", "owner@example.com", "owner", "eShopOnWeb Customer", Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(1, "owner@example.com", "owner@example.com"));
        _billingClient.FindLiveSubscriptionAsync(1, Arg.Any<CancellationToken>()).Returns(existing);

        var sut = CreateSut();

        var result = await sut.SubscribeAsync("owner@example.com", "owner@example.com", "owner", "eShopOnWeb Customer", "eshop-pro");

        Assert.Equal(existing.Id, result.Id);
        await _billingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
