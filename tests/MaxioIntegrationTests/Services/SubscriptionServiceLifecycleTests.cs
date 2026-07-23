using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Services;

/// <summary>UC4 — pause / resume / cancel / reactivate, with illegal transitions rejected locally.</summary>
public class SubscriptionServiceLifecycleTests
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly SubscriptionService _service;

    public SubscriptionServiceLifecycleTests()
    {
        _service = new SubscriptionService(_billingClient, _publisher, Substitute.For<IAppLogger<SubscriptionService>>());
    }

    [Fact]
    public async Task PausesAnActiveSubscriptionAndPublishesTheStateChange()
    {
        GivenSubscription(SubscriptionState.Active);
        _billingClient.PauseAsync(100, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(state: SubscriptionState.OnHold));

        var result = await Apply(SubscriptionLifecycleAction.Pause);

        Assert.Equal(SubscriptionState.OnHold, result.State);
        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionStateChanged>(n =>
                n.PreviousState == SubscriptionState.Active &&
                n.NewState == SubscriptionState.OnHold &&
                n.Action == SubscriptionLifecycleAction.Pause),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumesAPausedSubscription()
    {
        GivenSubscription(SubscriptionState.OnHold);
        _billingClient.ResumeAsync(100, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(state: SubscriptionState.Active));

        var result = await Apply(SubscriptionLifecycleAction.Resume);

        Assert.Equal(SubscriptionState.Active, result.State);
    }

    [Fact]
    public async Task CancelsImmediatelyWhenImmediateTimingIsRequested()
    {
        GivenSubscription(SubscriptionState.Active);
        _billingClient.CancelAsync(100, CancellationTiming.Immediate, "too expensive", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(state: SubscriptionState.Canceled));

        var result = await Apply(SubscriptionLifecycleAction.Cancel, CancellationTiming.Immediate, "too expensive");

        Assert.Equal(SubscriptionState.Canceled, result.State);
        Assert.False(result.CancelAtEndOfPeriod);
        await _billingClient.Received(1).CancelAsync(100, CancellationTiming.Immediate, "too expensive", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DefersCancellationToThePeriodBoundaryWhenEndOfPeriodIsRequested()
    {
        GivenSubscription(SubscriptionState.Active);
        _billingClient.CancelAsync(100, CancellationTiming.EndOfPeriod, null, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(state: SubscriptionState.Active, cancelAtEndOfPeriod: true));

        var result = await Apply(SubscriptionLifecycleAction.Cancel, CancellationTiming.EndOfPeriod);

        Assert.True(result.CancelAtEndOfPeriod);
        Assert.NotNull(result.DelayedCancelAt);
        Assert.Equal(SubscriptionState.Active, result.State);
    }

    [Fact]
    public async Task ReactivatesACancelledSubscription()
    {
        GivenSubscription(SubscriptionState.Canceled);
        _billingClient.ReactivateAsync(100, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(state: SubscriptionState.Active));

        var result = await Apply(SubscriptionLifecycleAction.Reactivate);

        Assert.Equal(SubscriptionState.Active, result.State);
    }

    [Theory]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Reactivate)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionState.Expired, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.OnHold, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Unknown, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Unknown, SubscriptionLifecycleAction.Cancel)]
    public async Task RejectsAnIllegalTransitionWithoutCallingTheProvider(SubscriptionState state, SubscriptionLifecycleAction action)
    {
        GivenSubscription(state);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(() => Apply(action));

        Assert.Contains(state.ToString(), exception.Message);
        Assert.Contains("Legal actions", exception.Message);

        await _billingClient.DidNotReceive().PauseAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _billingClient.DidNotReceive().ResumeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _billingClient.DidNotReceive().CancelAsync(Arg.Any<int>(), Arg.Any<CancellationTiming>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _billingClient.DidNotReceive().ReactivateAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().Publish(Arg.Any<SubscriptionStateChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsALifecycleActionOnAnUnknownSubscription()
    {
        _billingClient.GetSubscriptionAsync(9_999, Arg.Any<CancellationToken>()).Returns((Subscription?)null);

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.ApplyLifecycleActionAsync(
                9_999, SubscriptionBuilder.UserReference, SubscriptionLifecycleAction.Pause, CancellationTiming.Immediate, null));
    }

    [Fact]
    public async Task RefusesALifecycleActionOnSomebodyElsesSubscription()
    {
        _billingClient.GetSubscriptionAsync(200, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(id: 200, customerReference: "someone.else@microsoft.com"));

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.ApplyLifecycleActionAsync(
                200, SubscriptionBuilder.UserReference, SubscriptionLifecycleAction.Cancel, CancellationTiming.Immediate, null));

        await _billingClient.DidNotReceive().CancelAsync(
            Arg.Any<int>(), Arg.Any<CancellationTiming>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotRevealThatSomebodyElsesSubscriptionExists()
    {
        _billingClient.GetSubscriptionAsync(200, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(id: 200, customerReference: "someone.else@microsoft.com"));
        _billingClient.GetSubscriptionAsync(201, Arg.Any<CancellationToken>()).Returns((Subscription?)null);

        var forbidden = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.ApplyLifecycleActionAsync(
                200, SubscriptionBuilder.UserReference, SubscriptionLifecycleAction.Cancel, CancellationTiming.Immediate, null));

        var missing = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.ApplyLifecycleActionAsync(
                201, SubscriptionBuilder.UserReference, SubscriptionLifecycleAction.Cancel, CancellationTiming.Immediate, null));

        // "Belongs to somebody else" must be indistinguishable from "does not exist".
        Assert.Equal("Subscription 200 was not found.", forbidden.Message);
        Assert.Equal("Subscription 201 was not found.", missing.Message);
    }

    [Fact]
    public async Task HidesSomebodyElsesSubscriptionFromAScopedRead()
    {
        _billingClient.GetSubscriptionAsync(200, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(id: 200, customerReference: "someone.else@microsoft.com"));

        Assert.Null(await _service.GetSubscriptionAsync(200, SubscriptionBuilder.UserReference));
        Assert.NotNull(await _service.GetSubscriptionAsync(200, ownerReference: null));
    }

    [Fact]
    public async Task KeepsTheTransitionWhenTheInProcessNotificationFails()
    {
        GivenSubscription(SubscriptionState.Active);
        _billingClient.CancelAsync(100, CancellationTiming.Immediate, null, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(state: SubscriptionState.Canceled));
        _publisher.Publish(Arg.Any<SubscriptionStateChanged>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("handler exploded"));

        var result = await Apply(SubscriptionLifecycleAction.Cancel);

        Assert.Equal(SubscriptionState.Canceled, result.State);
    }

    [Fact]
    public async Task SurfacesAProviderRejectionOfATransitionTheLocalCheckAllowed()
    {
        GivenSubscription(SubscriptionState.Active);
        _billingClient.PauseAsync(100, Arg.Any<CancellationToken>())
            .Returns<Subscription>(_ => throw new BillingProviderException("Cannot hold within 24 hours of renewal", 422));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => Apply(SubscriptionLifecycleAction.Pause));

        Assert.Equal(422, exception.StatusCode);
        await _publisher.DidNotReceive().Publish(Arg.Any<SubscriptionStateChanged>(), Arg.Any<CancellationToken>());
    }

    private void GivenSubscription(SubscriptionState state) =>
        _billingClient.GetSubscriptionAsync(100, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(state: state));

    private Task<Subscription> Apply(SubscriptionLifecycleAction action,
        CancellationTiming timing = CancellationTiming.Immediate,
        string? reason = null) =>
        _service.ApplyLifecycleActionAsync(100, SubscriptionBuilder.UserReference, action, timing, reason);
}
