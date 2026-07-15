using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class LifecycleTransitions
{
    private readonly IBillingClient _mockBillingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _mockPublisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _mockLogger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly SubscriptionBuilder _builder = new();

    private SubscriptionService CreateService() => new(_mockBillingClient, _mockPublisher, _mockLogger);

    [Fact]
    public async Task Pause_ThrowsInvalidSubscriptionStateException_WhenAlreadyPaused()
    {
        var subscription = _builder.WithState(1, SubscriptionState.Paused);
        _mockBillingClient.GetSubscriptionAsync(1, default).Returns(subscription);

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidSubscriptionStateException>(() => service.PauseAsync(1, _builder.TestOwnerReference));
        await _mockBillingClient.DidNotReceive().PauseSubscriptionAsync(Arg.Any<int>(), default);
    }

    [Fact]
    public async Task Pause_SucceedsAndPublishesStateChanged_WhenActive()
    {
        var subscription = _builder.WithState(1, SubscriptionState.Active);
        _mockBillingClient.GetSubscriptionAsync(1, default).Returns(subscription);
        var paused = _builder.WithState(1, SubscriptionState.Paused);
        _mockBillingClient.PauseSubscriptionAsync(1, default).Returns(paused);

        var service = CreateService();

        var result = await service.PauseAsync(1, _builder.TestOwnerReference);

        Assert.Equal(SubscriptionState.Paused, result.State);
        await _mockPublisher.Received().Publish(
            Arg.Is<SubscriptionStateChanged>(n => n.SubscriptionId == 1 && n.PreviousState == SubscriptionState.Active && n.NewState == SubscriptionState.Paused),
            default);
    }

    [Fact]
    public async Task Pause_RefreshesStateAndThrowsInvalidSubscriptionStateException_WhenProviderRejectsTheTransition()
    {
        var subscription = _builder.WithState(1, SubscriptionState.Active);
        _mockBillingClient.GetSubscriptionAsync(1, default).Returns(subscription, _builder.WithState(1, SubscriptionState.Canceled));
        _mockBillingClient.PauseSubscriptionAsync(1, default).Returns(Task.FromException<Subscription>(new BillingProviderException("conflict")));

        var service = CreateService();

        var ex = await Assert.ThrowsAsync<InvalidSubscriptionStateException>(() => service.PauseAsync(1, _builder.TestOwnerReference));

        Assert.Equal(SubscriptionState.Canceled, ex.CurrentState);
        await _mockBillingClient.Received(2).GetSubscriptionAsync(1, default);
    }

    [Fact]
    public async Task Resume_ThrowsInvalidSubscriptionStateException_WhenNotPaused()
    {
        var subscription = _builder.WithState(1, SubscriptionState.Active);
        _mockBillingClient.GetSubscriptionAsync(1, default).Returns(subscription);

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidSubscriptionStateException>(() => service.ResumeAsync(1, _builder.TestOwnerReference));
    }

    [Fact]
    public async Task Reactivate_ThrowsInvalidSubscriptionStateException_WhenActive()
    {
        var subscription = _builder.WithState(1, SubscriptionState.Active);
        _mockBillingClient.GetSubscriptionAsync(1, default).Returns(subscription);

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidSubscriptionStateException>(() => service.ReactivateAsync(1, _builder.TestOwnerReference));
    }

    [Fact]
    public async Task Cancel_ThrowsInvalidSubscriptionStateException_WhenAlreadyCanceled()
    {
        var subscription = _builder.WithState(1, SubscriptionState.Canceled);
        _mockBillingClient.GetSubscriptionAsync(1, default).Returns(subscription);

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidSubscriptionStateException>(() =>
            service.CancelAsync(1, _builder.TestOwnerReference, CancellationTiming.Immediate, null));
    }

    [Fact]
    public async Task Cancel_PassesTimingAndReasonThrough_WhenLegal()
    {
        var subscription = _builder.WithState(1, SubscriptionState.Active);
        _mockBillingClient.GetSubscriptionAsync(1, default).Returns(subscription);
        var canceled = _builder.WithState(1, SubscriptionState.Canceled);
        _mockBillingClient.CancelSubscriptionAsync(1, CancellationTiming.EndOfPeriod, "no longer needed", default).Returns(canceled);

        var service = CreateService();

        var result = await service.CancelAsync(1, _builder.TestOwnerReference, CancellationTiming.EndOfPeriod, "no longer needed");

        Assert.Equal(SubscriptionState.Canceled, result.State);
        await _mockBillingClient.Received().CancelSubscriptionAsync(1, CancellationTiming.EndOfPeriod, "no longer needed", default);
    }
}
