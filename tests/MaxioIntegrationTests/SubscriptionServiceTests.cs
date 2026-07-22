using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Exercises the provider-agnostic seam: the use-case rules that hold whichever billing provider
/// sits behind <see cref="IBillingClient"/>.
/// </summary>
public class SubscriptionServiceTests
{
    private const string UserReference = "demouser@microsoft.com";

    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private readonly SubscriptionSettings _settings = new()
    {
        ProductFamilyHandle = "eshop-subscribe",
        DefaultProductHandle = "eshop-pro",
        AlternateProductHandle = "basic-plan",
        MeteredComponentHandle = "api-call"
    };

    private SubscriptionService CreateService() => new(_billingClient, _publisher, _logger, _settings);

    [Fact]
    public async Task SubscribingWithoutAPlanUsesTheConfiguredDefault()
    {
        GivenPlan("eshop-pro", 299.00m);
        _billingClient.FindCustomerByReferenceAsync(UserReference).ReturnsForAnyArgs((BillingCustomer?)null);
        _billingClient.CreateCustomerAsync(default!, default!).ReturnsForAnyArgs(new BillingCustomer { Id = 42 });
        _billingClient.ListCustomerSubscriptionsAsync(42).ReturnsForAnyArgs(Array.Empty<BillingSubscription>());
        _billingClient.CreateSubscriptionAsync(default, default!).ReturnsForAnyArgs(ActiveSubscription());

        var subscription = await CreateService().SubscribeAsync(UserReference, planHandle: null);

        await _billingClient.Received(1).CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<CancellationToken>());
        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.Equal("active", subscription.State);
        Assert.Equal(UserReference, subscription.BuyerId);
    }

    [Fact]
    public async Task SubscribingCreatesTheProviderCustomerOnlyWhenThereIsNoneYet()
    {
        GivenPlan("eshop-pro", 299.00m);
        _billingClient.FindCustomerByReferenceAsync(default!).ReturnsForAnyArgs(new BillingCustomer { Id = 42 });
        _billingClient.ListCustomerSubscriptionsAsync(42).ReturnsForAnyArgs(Array.Empty<BillingSubscription>());
        _billingClient.CreateSubscriptionAsync(default, default!).ReturnsForAnyArgs(ActiveSubscription());

        await CreateService().SubscribeAsync(UserReference, "eshop-pro");

        await _billingClient.DidNotReceiveWithAnyArgs().CreateCustomerAsync(default!, default!);
    }

    [Fact]
    public async Task ARepeatedSubscribeReturnsTheExistingEnrollmentInsteadOfCreatingASecond()
    {
        GivenPlan("eshop-pro", 299.00m);
        _billingClient.FindCustomerByReferenceAsync(default!).ReturnsForAnyArgs(new BillingCustomer { Id = 42 });
        _billingClient.ListCustomerSubscriptionsAsync(42).ReturnsForAnyArgs(new[] { ActiveSubscription() });

        var subscription = await CreateService().SubscribeAsync(UserReference, "eshop-pro");

        await _billingClient.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!);
        Assert.Equal(15236915, subscription.BillingSubscriptionId);
    }

    [Fact]
    public async Task AStalePlanHandleIsAConfigurationErrorAndNothingIsEnrolled()
    {
        _billingClient.GetPlanByHandleAsync(default!).ReturnsForAnyArgs((BillingPlan?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => CreateService().SubscribeAsync(UserReference, "gone-after-reseed"));

        await _billingClient.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!);
    }

    [Fact]
    public async Task ASuccessfulEnrollmentAnnouncesItselfInProcess()
    {
        GivenPlan("eshop-pro", 299.00m);
        _billingClient.FindCustomerByReferenceAsync(default!).ReturnsForAnyArgs(new BillingCustomer { Id = 42 });
        _billingClient.ListCustomerSubscriptionsAsync(42).ReturnsForAnyArgs(Array.Empty<BillingSubscription>());
        _billingClient.CreateSubscriptionAsync(default, default!).ReturnsForAnyArgs(ActiveSubscription());

        await CreateService().SubscribeAsync(UserReference, "eshop-pro");

        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionActivated>(e => e.BillingSubscriptionId == 15236915 && e.PlanPrice == 299.00m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailingNotificationHandlerDoesNotUndoTheEnrollment()
    {
        GivenPlan("eshop-pro", 299.00m);
        _billingClient.FindCustomerByReferenceAsync(default!).ReturnsForAnyArgs(new BillingCustomer { Id = 42 });
        _billingClient.ListCustomerSubscriptionsAsync(42).ReturnsForAnyArgs(Array.Empty<BillingSubscription>());
        _billingClient.CreateSubscriptionAsync(default, default!).ReturnsForAnyArgs(ActiveSubscription());
        _publisher.Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("handler blew up"));

        var subscription = await CreateService().SubscribeAsync(UserReference, "eshop-pro");

        Assert.Equal(15236915, subscription.BillingSubscriptionId);
    }

    [Fact]
    public async Task AUserWithNoProviderCustomerHasNoSubscriptions()
    {
        _billingClient.FindCustomerByReferenceAsync(default!).ReturnsForAnyArgs((BillingCustomer?)null);

        var subscriptions = await CreateService().ListSubscriptionsAsync(UserReference);

        Assert.Empty(subscriptions);
        await _billingClient.DidNotReceiveWithAnyArgs().ListCustomerSubscriptionsAsync(default);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task AnInvalidUsageQuantityIsRejectedBeforeAnyProviderCall(int quantity)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => CreateService().RecordUsageAsync(15236915, quantity, null));

        await _billingClient.DidNotReceiveWithAnyArgs().RecordUsageAsync(default, default!, default, default);
    }

    [Fact]
    public async Task UsageIsRefusedWhenTheConfiguredComponentIsNotMetered()
    {
        _billingClient.GetComponentByHandleAsync(default!)
            .ReturnsForAnyArgs(new BillingComponent { Handle = "api-call", Kind = "quantity_based_component" });

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => CreateService().RecordUsageAsync(15236915, 10, null));

        Assert.Contains("quantity_based_component", exception.Message, StringComparison.Ordinal);
        await _billingClient.DidNotReceiveWithAnyArgs().RecordUsageAsync(default, default!, default, default);
    }

    [Fact]
    public async Task UsageIsRefusedWhenTheCustomerHasNoActiveSubscription()
    {
        GivenMeteredComponent();
        _billingClient.GetSubscriptionAsync(default).ReturnsForAnyArgs(
            new BillingSubscription { Id = 15236915, State = "canceled" });

        await Assert.ThrowsAsync<BillingValidationException>(
            () => CreateService().RecordUsageAsync(15236915, 10, null));

        await _billingClient.DidNotReceiveWithAnyArgs().RecordUsageAsync(default, default!, default, default);
    }

    [Fact]
    public async Task AnUnknownSubscriptionIsRejectedAsNotFound()
    {
        GivenMeteredComponent();
        _billingClient.GetSubscriptionAsync(default).ReturnsForAnyArgs((BillingSubscription?)null);

        await Assert.ThrowsAsync<BillingEntityNotFoundException>(
            () => CreateService().RecordUsageAsync(404404, 10, null));
    }

    [Fact]
    public async Task RecordedUsageIsPricedAtTheComponentsUnitPrice()
    {
        GivenMeteredComponent();
        _billingClient.GetSubscriptionAsync(default).ReturnsForAnyArgs(ActiveSubscription());
        _billingClient.RecordUsageAsync(default, default!, default, default)
            .ReturnsForAnyArgs(new BillingUsageRecord { Id = 1, Quantity = 250 });
        _billingClient.GetUsageTotalAsync(default, default!)
            .ReturnsForAnyArgs(new BillingUsageTotal { UnitBalance = 250 });

        var result = await CreateService().RecordUsageAsync(15236915, 250, "order placed");

        Assert.True(result.PeriodToDateAvailable);
        Assert.Equal(250m, result.PeriodToDateUnits);
        // 250 calls at $0.01 each.
        Assert.Equal(2.50m, result.PeriodToDateAmount);
    }

    [Fact]
    public async Task AFailedReadBackLeavesTheUsageStandingWithTheTotalUnavailable()
    {
        GivenMeteredComponent();
        _billingClient.GetSubscriptionAsync(default).ReturnsForAnyArgs(ActiveSubscription());
        _billingClient.RecordUsageAsync(default, default!, default, default)
            .ReturnsForAnyArgs(new BillingUsageRecord { Id = 77, Quantity = 250 });
        _billingClient.GetUsageTotalAsync(default, default!)
            .ThrowsAsyncForAnyArgs(new BillingProviderUnavailableException("read-back failed"));

        var result = await CreateService().RecordUsageAsync(15236915, 250, null);

        Assert.Equal(77, result.UsageRecordId);
        Assert.Equal(250m, result.QuantityRecorded);
        Assert.False(result.PeriodToDateAvailable);
        Assert.Null(result.PeriodToDateUnits);
    }

    [Fact]
    public async Task ChangingToThePlanAlreadyInForceIsRejectedAsANoOp()
    {
        _billingClient.GetSubscriptionAsync(default).ReturnsForAnyArgs(ActiveSubscription());

        await Assert.ThrowsAsync<BillingValidationException>(
            () => CreateService().PreviewPlanChangeAsync(15236915, "eshop-pro", PlanChangeTiming.ImmediateWithProration));

        await _billingClient.DidNotReceiveWithAnyArgs().PreviewPlanChangeAsync(default, default!, default);
    }

    [Fact]
    public async Task APlanChangeOnACancelledSubscriptionIsRejected()
    {
        _billingClient.GetSubscriptionAsync(default).ReturnsForAnyArgs(
            new BillingSubscription { Id = 15236915, State = "canceled", ProductHandle = "eshop-pro" });

        await Assert.ThrowsAsync<BillingValidationException>(
            () => CreateService().ChangePlanAsync(15236915, "basic-plan", PlanChangeTiming.AtNextRenewal, null));

        await _billingClient.DidNotReceiveWithAnyArgs().ChangePlanAsync(default, default!, default);
    }

    [Fact]
    public async Task AStalePreviewIsRejectedRatherThanChargingADifferentAmount()
    {
        GivenPlan("basic-plan", 29.00m);
        _billingClient.GetSubscriptionAsync(default).ReturnsForAnyArgs(ActiveSubscription());
        _billingClient.PreviewPlanChangeAsync(default, default!, default)
            .ReturnsForAnyArgs(new BillingPlanChangePreview { PaymentDue = 199.00m });

        var exception = await Assert.ThrowsAsync<BillingValidationException>(
            () => CreateService().ChangePlanAsync(15236915, "basic-plan", PlanChangeTiming.ImmediateWithProration, 135.00m));

        Assert.Contains("stale", exception.Message, StringComparison.OrdinalIgnoreCase);
        await _billingClient.DidNotReceiveWithAnyArgs().ChangePlanAsync(default, default!, default);
    }

    [Fact]
    public async Task AConfirmedPreviewThatStillHoldsIsCommittedAndAnnounced()
    {
        GivenPlan("basic-plan", 29.00m);
        _billingClient.GetSubscriptionAsync(default).ReturnsForAnyArgs(ActiveSubscription());
        _billingClient.PreviewPlanChangeAsync(default, default!, default)
            .ReturnsForAnyArgs(new BillingPlanChangePreview { PaymentDue = 135.00m });
        _billingClient.ChangePlanAsync(default, default!, default).ReturnsForAnyArgs(
            new BillingSubscription { Id = 15236915, State = "active", ProductHandle = "basic-plan", ProductPrice = 29.00m });

        var result = await CreateService().ChangePlanAsync(
            15236915, "basic-plan", PlanChangeTiming.ImmediateWithProration, 135.00m);

        Assert.Equal("eshop-pro", result.OldPlanHandle);
        Assert.Equal("basic-plan", result.NewPlanHandle);
        await _publisher.Received(1).Publish(Arg.Any<SubscriptionPlanChanged>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(SubscriptionLifecycleAction.Resume, "active")]
    [InlineData(SubscriptionLifecycleAction.Reactivate, "active")]
    [InlineData(SubscriptionLifecycleAction.Pause, "canceled")]
    [InlineData(SubscriptionLifecycleAction.Resume, "canceled")]
    public async Task AnIllegalTransitionIsRejectedWithoutTouchingTheProvider(SubscriptionLifecycleAction action, string state)
    {
        _billingClient.GetSubscriptionAsync(default).ReturnsForAnyArgs(
            new BillingSubscription { Id = 15236915, State = state, ProductHandle = "eshop-pro" });

        var exception = await Assert.ThrowsAsync<BillingValidationException>(
            () => CreateService().ApplyLifecycleActionAsync(15236915, action, CancellationTiming.Immediate, null));

        Assert.Contains(state, exception.Message, StringComparison.Ordinal);
        await _billingClient.DidNotReceiveWithAnyArgs().PauseSubscriptionAsync(default);
        await _billingClient.DidNotReceiveWithAnyArgs().ResumeSubscriptionAsync(default);
        await _billingClient.DidNotReceiveWithAnyArgs().ReactivateSubscriptionAsync(default);
    }

    [Fact]
    public async Task PausingALiveSubscriptionAnnouncesTheOldAndNewState()
    {
        _billingClient.GetSubscriptionAsync(default).ReturnsForAnyArgs(ActiveSubscription());
        _billingClient.PauseSubscriptionAsync(default).ReturnsForAnyArgs(
            new BillingSubscription { Id = 15236915, State = "on_hold", ProductHandle = "eshop-pro" });

        var subscription = await CreateService()
            .ApplyLifecycleActionAsync(15236915, SubscriptionLifecycleAction.Pause, CancellationTiming.Immediate, null);

        Assert.Equal("on_hold", subscription.State);
        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionStateChanged>(e => e.OldState == "active" && e.NewState == "on_hold"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnEndOfPeriodCancelDefersToThePeriodBoundary()
    {
        _billingClient.GetSubscriptionAsync(default).ReturnsForAnyArgs(ActiveSubscription());
        _billingClient.CancelSubscriptionAtEndOfPeriodAsync(default, default).ReturnsForAnyArgs(
            new BillingSubscription { Id = 15236915, State = "active", ProductHandle = "eshop-pro", CancelAtEndOfPeriod = true });

        var subscription = await CreateService().ApplyLifecycleActionAsync(
            15236915, SubscriptionLifecycleAction.Cancel, CancellationTiming.EndOfPeriod, "too expensive");

        Assert.True(subscription.CancelAtEndOfPeriod);
        await _billingClient.Received(1).CancelSubscriptionAtEndOfPeriodAsync(15236915, "too expensive", Arg.Any<CancellationToken>());
        await _billingClient.DidNotReceiveWithAnyArgs().CancelSubscriptionAsync(default, default);
    }

    [Fact]
    public async Task AnImmediateCancelStopsTheSubscriptionNow()
    {
        _billingClient.GetSubscriptionAsync(default).ReturnsForAnyArgs(ActiveSubscription());
        _billingClient.CancelSubscriptionAsync(default, default).ReturnsForAnyArgs(
            new BillingSubscription { Id = 15236915, State = "canceled", ProductHandle = "eshop-pro" });

        var subscription = await CreateService().ApplyLifecycleActionAsync(
            15236915, SubscriptionLifecycleAction.Cancel, CancellationTiming.Immediate, null);

        Assert.Equal("canceled", subscription.State);
        await _billingClient.DidNotReceiveWithAnyArgs().CancelSubscriptionAtEndOfPeriodAsync(default, default);
    }

    private void GivenPlan(string handle, decimal price)
        => _billingClient.GetPlanByHandleAsync(handle, Arg.Any<CancellationToken>())
            .Returns(new BillingPlan { Handle = handle, Name = handle, Price = price });

    private void GivenMeteredComponent()
        => _billingClient.GetComponentByHandleAsync(default!).ReturnsForAnyArgs(
            new BillingComponent { Id = 3057195, Handle = "api-call", Kind = BillingComponent.MeteredKind, UnitPrice = 0.01m });

    private static BillingSubscription ActiveSubscription() => new()
    {
        Id = 15236915,
        State = "active",
        CustomerId = 42,
        CustomerReference = UserReference,
        ProductHandle = "eshop-pro",
        ProductName = "Pro Plan",
        ProductPrice = 299.00m
    };
}
