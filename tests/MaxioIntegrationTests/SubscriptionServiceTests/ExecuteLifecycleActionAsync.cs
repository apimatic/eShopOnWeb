using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

public class ExecuteLifecycleActionAsync
{
    private readonly SubscriptionServiceFixture _fixture = new();

    private void ArrangeSubscriptionIn(SubscriptionState state)
    {
        _fixture.BillingClient.FindCustomerByReferenceAsync(SubscriptionServiceFixture.UserReference,
                Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.Customer());
        _fixture.BillingClient.ListSubscriptionsAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionServiceFixture.SubscriptionIn(state) });
    }

    private Task<Subscription> Act(SubscriptionLifecycleAction action,
        CancellationTiming timing = CancellationTiming.Immediate) =>
        _fixture.CreateService().ExecuteLifecycleActionAsync(SubscriptionServiceFixture.UserReference,
            action, timing, "because");

    [Fact]
    public async Task PausesAnActiveSubscriptionAndAnnouncesTheOldAndNewState()
    {
        ArrangeSubscriptionIn(SubscriptionState.Active);
        _fixture.BillingClient.PauseSubscriptionAsync(90210, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Paused));

        var updated = await Act(SubscriptionLifecycleAction.Pause);

        Assert.Equal(SubscriptionState.Paused, updated.State);
        await _fixture.Publisher.Received(1).Publish(
            Arg.Is<SubscriptionStateChanged>(n =>
                n.PreviousState == SubscriptionState.Active && n.NewState == SubscriptionState.Paused),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumesAPausedSubscription()
    {
        ArrangeSubscriptionIn(SubscriptionState.Paused);
        _fixture.BillingClient.ResumeSubscriptionAsync(90210, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Active));

        var updated = await Act(SubscriptionLifecycleAction.Resume);

        Assert.Equal(SubscriptionState.Active, updated.State);
    }

    [Fact]
    public async Task PassesTheChosenCancellationTimingThrough()
    {
        ArrangeSubscriptionIn(SubscriptionState.Active);
        _fixture.BillingClient.CancelSubscriptionAsync(90210, CancellationTiming.EndOfPeriod, "because",
                Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Active));

        await Act(SubscriptionLifecycleAction.Cancel, CancellationTiming.EndOfPeriod);

        await _fixture.BillingClient.Received(1)
            .CancelSubscriptionAsync(90210, CancellationTiming.EndOfPeriod, "because", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReactivatesACancelledSubscription()
    {
        ArrangeSubscriptionIn(SubscriptionState.Canceled);
        _fixture.BillingClient.ReactivateSubscriptionAsync(90210, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Active));

        var updated = await Act(SubscriptionLifecycleAction.Reactivate);

        Assert.Equal(SubscriptionState.Active, updated.State);
    }

    [Theory]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Reactivate)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Paused, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Expired, SubscriptionLifecycleAction.Cancel)]
    public async Task RejectsAnIllegalTransitionWithoutCallingTheProvider(SubscriptionState state,
        SubscriptionLifecycleAction action)
    {
        ArrangeSubscriptionIn(state);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(() => Act(action));

        Assert.Equal(state, exception.CurrentState);
        Assert.Equal(action, exception.RequestedAction);
        Assert.DoesNotContain(action, exception.AllowedActions);

        await _fixture.BillingClient.DidNotReceive().PauseSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _fixture.BillingClient.DidNotReceive().ResumeSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _fixture.BillingClient.DidNotReceive().ReactivateSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _fixture.BillingClient.DidNotReceive().CancelSubscriptionAsync(Arg.Any<int>(),
            Arg.Any<CancellationTiming>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _fixture.Publisher.DidNotReceive()
            .Publish(Arg.Any<SubscriptionStateChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportsThatAUserWithNoSubscriptionHasNothingToManage()
    {
        _fixture.BillingClient.FindCustomerByReferenceAsync(SubscriptionServiceFixture.UserReference,
                Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(() => Act(SubscriptionLifecycleAction.Cancel));
    }
}
