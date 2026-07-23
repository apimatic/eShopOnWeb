using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

public class LifecycleActions
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly SubscriptionService _service;

    public LifecycleActions()
    {
        _service = new SubscriptionService(_billingClient, _publisher, new NullAppLogger<SubscriptionService>());

        _billingClient.FindCustomerByReferenceAsync(TestData.BuyerId, Arg.Any<CancellationToken>())
            .Returns(TestData.Customer);
    }

    [Fact]
    public async Task PausesAnActiveSubscription()
    {
        ArrangeState(SubscriptionState.Active);
        _billingClient.PauseAsync(TestData.SubscriptionId, Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription(SubscriptionState.Paused));

        var updated = await _service.ApplyLifecycleActionAsync(
            TestData.BuyerId, TestData.SubscriptionId, SubscriptionLifecycleAction.Pause);

        Assert.Equal(SubscriptionState.Paused, updated.State);
    }

    [Fact]
    public async Task ResumesAPausedSubscription()
    {
        ArrangeState(SubscriptionState.Paused);
        _billingClient.ResumeAsync(TestData.SubscriptionId, Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription(SubscriptionState.Active));

        var updated = await _service.ApplyLifecycleActionAsync(
            TestData.BuyerId, TestData.SubscriptionId, SubscriptionLifecycleAction.Resume);

        Assert.Equal(SubscriptionState.Active, updated.State);
    }

    [Fact]
    public async Task ReactivatesACancelledSubscription()
    {
        ArrangeState(SubscriptionState.Canceled);
        _billingClient.ReactivateAsync(TestData.SubscriptionId, Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription(SubscriptionState.Active));

        var updated = await _service.ApplyLifecycleActionAsync(
            TestData.BuyerId, TestData.SubscriptionId, SubscriptionLifecycleAction.Reactivate);

        Assert.Equal(SubscriptionState.Active, updated.State);
    }

    [Fact]
    public async Task CancelsImmediatelyWhenAskedTo()
    {
        ArrangeState(SubscriptionState.Active);
        _billingClient.CancelAsync(TestData.SubscriptionId, CancellationTiming.Immediate, "done", Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription(SubscriptionState.Canceled));

        var updated = await _service.ApplyLifecycleActionAsync(
            TestData.BuyerId, TestData.SubscriptionId, SubscriptionLifecycleAction.Cancel,
            CancellationTiming.Immediate, "done");

        Assert.Equal(SubscriptionState.Canceled, updated.State);
    }

    /// <summary>
    /// An end-of-period cancel defers to the boundary: the subscription is still active and the
    /// announced effective date is the boundary, not the moment of the request.
    /// </summary>
    [Fact]
    public async Task DefersAnEndOfPeriodCancelToThePeriodBoundary()
    {
        ArrangeState(SubscriptionState.Active);
        var boundary = TestData.PeriodEnd;
        _billingClient.CancelAsync(TestData.SubscriptionId, CancellationTiming.EndOfPeriod, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription(cancelAtEndOfPeriod: true, delayedCancelAt: boundary));

        var updated = await _service.ApplyLifecycleActionAsync(
            TestData.BuyerId, TestData.SubscriptionId, SubscriptionLifecycleAction.Cancel, CancellationTiming.EndOfPeriod);

        Assert.Equal(SubscriptionState.Active, updated.State);
        Assert.True(updated.Billing.CancelAtEndOfPeriod);

        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionStateChanged>(n => n.EffectiveAt == boundary),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishesTheOldAndNewStateOnEveryTransition()
    {
        ArrangeState(SubscriptionState.Active);
        _billingClient.PauseAsync(TestData.SubscriptionId, Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription(SubscriptionState.Paused));

        await _service.ApplyLifecycleActionAsync(
            TestData.BuyerId, TestData.SubscriptionId, SubscriptionLifecycleAction.Pause, reason: "on holiday");

        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionStateChanged>(n =>
                n.BuyerId == TestData.BuyerId &&
                n.SubscriptionId == TestData.SubscriptionId &&
                n.PreviousState == SubscriptionState.Active &&
                n.NewState == SubscriptionState.Paused &&
                n.Action == "Pause" &&
                n.Reason == "on holiday"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Illegal transitions are rejected locally, so the provider is never asked to do something the
    /// current state forbids.
    /// </summary>
    [Theory]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Expired, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Paused, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Reactivate)]
    [InlineData(SubscriptionState.Paused, SubscriptionLifecycleAction.Reactivate)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Cancel)]
    [InlineData(SubscriptionState.Expired, SubscriptionLifecycleAction.Cancel)]
    public async Task RejectsAnIllegalTransitionWithoutCallingTheProvider(SubscriptionState state, SubscriptionLifecycleAction action)
    {
        ArrangeState(state);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(() =>
            _service.ApplyLifecycleActionAsync(TestData.BuyerId, TestData.SubscriptionId, action));

        // The message names the current state and what the action requires.
        Assert.Contains(state.ToString(), exception.Message);

        await _billingClient.DidNotReceiveWithAnyArgs().PauseAsync(default, default);
        await _billingClient.DidNotReceiveWithAnyArgs().ResumeAsync(default, default);
        await _billingClient.DidNotReceiveWithAnyArgs().CancelAsync(default, default, default, default);
        await _billingClient.DidNotReceiveWithAnyArgs().ReactivateAsync(default, default);
        await _publisher.DidNotReceiveWithAnyArgs().Publish(default(SubscriptionStateChanged)!, default);
    }

    [Theory]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Trialing, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Paused, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Reactivate)]
    [InlineData(SubscriptionState.Expired, SubscriptionLifecycleAction.Reactivate)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Cancel)]
    [InlineData(SubscriptionState.PastDue, SubscriptionLifecycleAction.Cancel)]
    [InlineData(SubscriptionState.Paused, SubscriptionLifecycleAction.Cancel)]
    public async Task AllowsALegalTransition(SubscriptionState state, SubscriptionLifecycleAction action)
    {
        ArrangeState(state);
        ArrangeAnyTransitionSucceeds();

        var updated = await _service.ApplyLifecycleActionAsync(TestData.BuyerId, TestData.SubscriptionId, action);

        Assert.NotNull(updated);
    }

    [Fact]
    public async Task RejectsAnActionOnASubscriptionBelongingToSomeoneElse()
    {
        ArrangeState(SubscriptionState.Active);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(() =>
            _service.ApplyLifecycleActionAsync(TestData.BuyerId, 999999, SubscriptionLifecycleAction.Pause));

        Assert.Contains("does not belong to", exception.Message);
    }

    /// <summary>
    /// State can drift out-of-band because there are no webhooks. When the provider refuses a
    /// transition the local check allowed, the provider's state wins and is reported.
    /// </summary>
    [Fact]
    public async Task SurfacesTheProvidersRefreshedStateWhenItRefusesTheTransition()
    {
        ArrangeState(SubscriptionState.Active);
        _billingClient.PauseAsync(TestData.SubscriptionId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("Only active subscriptions can be put on hold.", 422, new[] { "bad state" }));
        _billingClient.GetSubscriptionAsync(TestData.SubscriptionId, Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription(SubscriptionState.Canceled));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() =>
            _service.ApplyLifecycleActionAsync(TestData.BuyerId, TestData.SubscriptionId, SubscriptionLifecycleAction.Pause));

        Assert.Contains("currently Canceled", exception.Message);
        Assert.Contains("Only active subscriptions can be put on hold.", exception.Message);
    }

    /// <summary>If the refresh itself fails there is nothing truer to report, so the original error stands.</summary>
    [Fact]
    public async Task RethrowsTheOriginalFailureWhenTheStateRefreshAlsoFails()
    {
        ArrangeState(SubscriptionState.Active);
        _billingClient.PauseAsync(TestData.SubscriptionId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("gateway timeout"));
        _billingClient.GetSubscriptionAsync(TestData.SubscriptionId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("still unreachable"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() =>
            _service.ApplyLifecycleActionAsync(TestData.BuyerId, TestData.SubscriptionId, SubscriptionLifecycleAction.Pause));

        Assert.Equal("gateway timeout", exception.Message);
    }

    [Fact]
    public async Task KeepsTheTransitionWhenTheNotificationHandlerFails()
    {
        ArrangeState(SubscriptionState.Active);
        _billingClient.PauseAsync(TestData.SubscriptionId, Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription(SubscriptionState.Paused));
        _publisher.Publish(Arg.Any<SubscriptionStateChanged>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("handler failed"));

        var updated = await _service.ApplyLifecycleActionAsync(
            TestData.BuyerId, TestData.SubscriptionId, SubscriptionLifecycleAction.Pause);

        Assert.Equal(SubscriptionState.Paused, updated.State);
    }

    private void ArrangeState(SubscriptionState state) =>
        _billingClient.ListSubscriptionsForCustomerAsync(TestData.CustomerId, Arg.Any<CancellationToken>())
            .Returns(new[] { TestData.Subscription(state) });

    private void ArrangeAnyTransitionSucceeds()
    {
        _billingClient.PauseAsync(TestData.SubscriptionId, Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription(SubscriptionState.Paused));
        _billingClient.ResumeAsync(TestData.SubscriptionId, Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription(SubscriptionState.Active));
        _billingClient.ReactivateAsync(TestData.SubscriptionId, Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription(SubscriptionState.Active));
        _billingClient.CancelAsync(TestData.SubscriptionId, Arg.Any<CancellationTiming>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription(SubscriptionState.Canceled));
    }
}
