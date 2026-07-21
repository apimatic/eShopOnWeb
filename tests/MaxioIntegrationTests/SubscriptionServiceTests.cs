using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Exercises <see cref="SubscriptionService"/>'s orchestration and guard logic — duplicate-enrollment
/// detection (UC1) and illegal-transition rejection (UC3/UC4) — against a fake <see cref="IBillingClient"/>,
/// the provider-agnostic seam this integration is built on.
/// </summary>
public class SubscriptionServiceTests
{
    private static Subscription MakeSubscription(int id, string productHandle, SubscriptionStatus status) =>
        new(id, productHandle, "Some Plan", 29900, status, null, null, false, null);

    private static SubscriptionService CreateService(IBillingClient billingClient) =>
        new(billingClient, Substitute.For<IPublisher>(), Substitute.For<IAppLogger<SubscriptionService>>());

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task RecordUsageAsync_RejectsNonPositiveQuantity_WithoutCallingBillingClient(int quantity)
    {
        var billingClient = Substitute.For<IBillingClient>();
        var service = CreateService(billingClient);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => service.RecordUsageAsync(1, quantity, memo: null));

        Assert.Equal(BillingErrorKind.Validation, ex.Kind);
        await billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingSubscription_WithoutCreatingDuplicate_WhenAlreadyActive()
    {
        var billingClient = Substitute.For<IBillingClient>();
        var existing = MakeSubscription(1, "eshop-pro", SubscriptionStatus.Active);
        billingClient.EnsureCustomerAsync("user@example.com", "user@example.com", "First", "Last", Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(10, "user@example.com", "user@example.com"));
        billingClient.ListCustomerSubscriptionsAsync(10, Arg.Any<CancellationToken>())
            .Returns(new List<Subscription> { existing });

        var service = CreateService(billingClient);
        var result = await service.SubscribeAsync("user@example.com", "user@example.com", "First", "Last", "eshop-pro");

        Assert.Same(existing, result);
        await billingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_CreatesNewSubscription_AndPublishesActivatedNotification_WhenNoneExists()
    {
        var billingClient = Substitute.For<IBillingClient>();
        var created = MakeSubscription(2, "eshop-pro", SubscriptionStatus.Active);
        billingClient.EnsureCustomerAsync("user@example.com", "user@example.com", "First", "Last", Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(10, "user@example.com", "user@example.com"));
        billingClient.ListCustomerSubscriptionsAsync(10, Arg.Any<CancellationToken>())
            .Returns(new List<Subscription>());
        billingClient.CreateSubscriptionAsync(10, "user@example.com", "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(created);

        var publisher = Substitute.For<IPublisher>();
        var service = new SubscriptionService(billingClient, publisher, Substitute.For<IAppLogger<SubscriptionService>>());

        var result = await service.SubscribeAsync("user@example.com", "user@example.com", "First", "Last", "eshop-pro");

        Assert.Same(created, result);
        await publisher.Received(1).Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitPlanChangeAsync_RejectsNoOp_WhenTargetIsAlreadyTheCurrentPlan()
    {
        var billingClient = Substitute.For<IBillingClient>();
        billingClient.GetSubscriptionAsync(1, Arg.Any<CancellationToken>())
            .Returns(MakeSubscription(1, "eshop-pro", SubscriptionStatus.Active));

        var service = CreateService(billingClient);
        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => service.CommitPlanChangeAsync("user@example.com", 1, "eshop-pro", applyNow: true));

        Assert.Equal(BillingErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task CommitPlanChangeAsync_RejectsChange_WhenSubscriptionIsCancelled()
    {
        var billingClient = Substitute.For<IBillingClient>();
        billingClient.GetSubscriptionAsync(1, Arg.Any<CancellationToken>())
            .Returns(MakeSubscription(1, "eshop-pro", SubscriptionStatus.Canceled));

        var service = CreateService(billingClient);
        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => service.CommitPlanChangeAsync("user@example.com", 1, "basic-plan", applyNow: true));

        Assert.Equal(BillingErrorKind.ProviderRejected, ex.Kind);
        await billingClient.DidNotReceive().CommitPlanChangeNowAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PauseAsync_RejectsTransition_WhenSubscriptionIsAlreadyOnHold()
    {
        var billingClient = Substitute.For<IBillingClient>();
        billingClient.GetSubscriptionAsync(1, Arg.Any<CancellationToken>())
            .Returns(MakeSubscription(1, "eshop-pro", SubscriptionStatus.OnHold));

        var service = CreateService(billingClient);
        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => service.PauseAsync("user@example.com", 1));

        Assert.Equal(BillingErrorKind.ProviderRejected, ex.Kind);
        await billingClient.DidNotReceive().PauseSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_RejectsTransition_WhenSubscriptionIsNotOnHold()
    {
        var billingClient = Substitute.For<IBillingClient>();
        billingClient.GetSubscriptionAsync(1, Arg.Any<CancellationToken>())
            .Returns(MakeSubscription(1, "eshop-pro", SubscriptionStatus.Active));

        var service = CreateService(billingClient);
        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => service.ResumeAsync("user@example.com", 1));

        Assert.Equal(BillingErrorKind.ProviderRejected, ex.Kind);
    }

    [Fact]
    public async Task ReactivateAsync_AllowsTransition_FromCanceled()
    {
        var billingClient = Substitute.For<IBillingClient>();
        var canceled = MakeSubscription(1, "eshop-pro", SubscriptionStatus.Canceled);
        var reactivated = MakeSubscription(1, "eshop-pro", SubscriptionStatus.Active);
        billingClient.GetSubscriptionAsync(1, Arg.Any<CancellationToken>()).Returns(canceled);
        billingClient.ReactivateSubscriptionAsync(1, Arg.Any<CancellationToken>()).Returns(reactivated);

        var service = CreateService(billingClient);
        var result = await service.ReactivateAsync("user@example.com", 1);

        Assert.Equal(SubscriptionStatus.Active, result.Status);
    }

    [Fact]
    public async Task ReactivateAsync_RejectsTransition_FromActive()
    {
        var billingClient = Substitute.For<IBillingClient>();
        billingClient.GetSubscriptionAsync(1, Arg.Any<CancellationToken>())
            .Returns(MakeSubscription(1, "eshop-pro", SubscriptionStatus.Active));

        var service = CreateService(billingClient);
        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => service.ReactivateAsync("user@example.com", 1));

        Assert.Equal(BillingErrorKind.ProviderRejected, ex.Kind);
        await billingClient.DidNotReceive().ReactivateSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
