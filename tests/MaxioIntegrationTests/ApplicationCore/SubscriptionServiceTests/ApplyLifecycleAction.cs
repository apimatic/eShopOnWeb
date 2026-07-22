using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.ApplicationCore.SubscriptionServiceTests;

public class ApplyLifecycleAction
{
    private readonly SubscriptionServiceBuilder _builder = new();

    private void CurrentStateIs(SubscriptionState state) =>
        _builder.BillingClient.GetSubscriptionAsync(15236915, Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().InState(state).Build());

    [Fact]
    public async Task PausesAnActiveSubscriptionAndPublishesTheTransition()
    {
        CurrentStateIs(SubscriptionState.Active);
        _builder.BillingClient.PauseAsync(15236915, null, Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().InState(SubscriptionState.OnHold).Build());

        var result = await _builder.Build()
            .ApplyLifecycleActionAsync(15236915, SubscriptionLifecycleAction.Pause);

        Assert.Equal(SubscriptionState.OnHold, result.State);

        await _builder.Publisher.Received(1).Publish(
            Arg.Is<SubscriptionStateChanged>(changed =>
                changed.PreviousState == SubscriptionState.Active &&
                changed.NewState == SubscriptionState.OnHold &&
                changed.Action == SubscriptionLifecycleAction.Pause),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumesAPausedSubscription()
    {
        CurrentStateIs(SubscriptionState.OnHold);
        _builder.BillingClient.ResumeAsync(15236915, Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().Build());

        var result = await _builder.Build()
            .ApplyLifecycleActionAsync(15236915, SubscriptionLifecycleAction.Resume);

        Assert.Equal(SubscriptionState.Active, result.State);
    }

    [Fact]
    public async Task CancelsAtTheEndOfThePeriodWhenAsked()
    {
        CurrentStateIs(SubscriptionState.Active);
        _builder.BillingClient.CancelAsync(15236915, true, "Too expensive", Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().CancellingAtPeriodEnd().Build());

        var result = await _builder.Build().ApplyLifecycleActionAsync(15236915,
            SubscriptionLifecycleAction.Cancel, cancelAtEndOfPeriod: true, reason: "Too expensive");

        Assert.True(result.CancelAtEndOfPeriod);
        Assert.Equal(SubscriptionState.Active, result.State);
    }

    [Fact]
    public async Task CancelsImmediatelyByDefault()
    {
        CurrentStateIs(SubscriptionState.Active);
        _builder.BillingClient.CancelAsync(15236915, false, null, Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().InState(SubscriptionState.Canceled).Build());

        var result = await _builder.Build()
            .ApplyLifecycleActionAsync(15236915, SubscriptionLifecycleAction.Cancel);

        Assert.Equal(SubscriptionState.Canceled, result.State);
    }

    [Fact]
    public async Task ReactivatesACancelledSubscription()
    {
        CurrentStateIs(SubscriptionState.Canceled);
        _builder.BillingClient.ReactivateAsync(15236915, Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().Build());

        var result = await _builder.Build()
            .ApplyLifecycleActionAsync(15236915, SubscriptionLifecycleAction.Reactivate);

        Assert.Equal(SubscriptionState.Active, result.State);
    }

    [Theory]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Reactivate)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionState.OnHold, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Expired, SubscriptionLifecycleAction.Cancel)]
    public async Task RejectsAnIllegalTransitionWithoutCallingTheProvider(SubscriptionState state,
        SubscriptionLifecycleAction action)
    {
        CurrentStateIs(state);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _builder.Build().ApplyLifecycleActionAsync(15236915, action));

        Assert.Equal(state, exception.CurrentState);
        Assert.Equal(action, exception.Action);

        await _builder.BillingClient.DidNotReceive().PauseAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset?>(),
            Arg.Any<CancellationToken>());
        await _builder.BillingClient.DidNotReceive().ResumeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _builder.BillingClient.DidNotReceive().CancelAsync(Arg.Any<int>(), Arg.Any<bool>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _builder.BillingClient.DidNotReceive().ReactivateAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TellsTheActorWhichActionsAreLegalFromTheCurrentState()
    {
        CurrentStateIs(SubscriptionState.Canceled);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _builder.Build().ApplyLifecycleActionAsync(15236915, SubscriptionLifecycleAction.Resume));

        Assert.Equal("Reactivate", exception.LegalActions);
        Assert.Contains("Reactivate", exception.Message);
    }

    [Fact]
    public async Task RejectsALifecycleActionOnAnUnknownSubscription()
    {
        _builder.BillingClient.GetSubscriptionAsync(404404, Arg.Any<CancellationToken>())
            .Returns((Subscription?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(() => _builder.Build()
            .ApplyLifecycleActionAsync(404404, SubscriptionLifecycleAction.Cancel));
    }
}
