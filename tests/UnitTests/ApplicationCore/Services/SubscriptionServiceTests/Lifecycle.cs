using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class Lifecycle
{
    private readonly IBillingClient _mockBillingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _mockPublisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _mockLogger = Substitute.For<IAppLogger<SubscriptionService>>();

    private SubscriptionService CreateSubscriptionService() =>
        new(_mockBillingClient, _mockPublisher, _mockLogger);

    private static CustomerSubscription SubscriptionWithState(string state) =>
        new(10, "buyer@test.com", state, "eshop-pro", "Pro Plan", 29900, null, null, false, null, 0);

    [Fact]
    public async Task PauseAsync_WhenAlreadyPaused_ThrowsConflictWithoutCallingProvider()
    {
        _mockBillingClient.GetSubscriptionAsync(10, Arg.Any<CancellationToken>())
            .Returns(SubscriptionWithState(SubscriptionStates.OnHold));

        var subscriptionService = CreateSubscriptionService();

        await Assert.ThrowsAsync<SubscriptionConflictException>(() =>
            subscriptionService.PauseAsync("buyer@test.com", 10));

        await _mockBillingClient.DidNotReceive().PauseSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_WhenNotPaused_ThrowsConflict()
    {
        _mockBillingClient.GetSubscriptionAsync(10, Arg.Any<CancellationToken>())
            .Returns(SubscriptionWithState(SubscriptionStates.Active));

        var subscriptionService = CreateSubscriptionService();

        await Assert.ThrowsAsync<SubscriptionConflictException>(() =>
            subscriptionService.ResumeAsync("buyer@test.com", 10));
    }

    [Fact]
    public async Task ReactivateAsync_WhenSubscriptionIsActive_ThrowsConflict()
    {
        _mockBillingClient.GetSubscriptionAsync(10, Arg.Any<CancellationToken>())
            .Returns(SubscriptionWithState(SubscriptionStates.Active));

        var subscriptionService = CreateSubscriptionService();

        await Assert.ThrowsAsync<SubscriptionConflictException>(() =>
            subscriptionService.ReactivateAsync("buyer@test.com", 10));

        await _mockBillingClient.DidNotReceive().ReactivateSubscriptionAsync(Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReactivateAsync_WhenCancelled_CallsProviderAndPublishesStateChange()
    {
        var reactivated = SubscriptionWithState(SubscriptionStates.Active);
        _mockBillingClient.GetSubscriptionAsync(10, Arg.Any<CancellationToken>())
            .Returns(SubscriptionWithState(SubscriptionStates.Canceled));
        _mockBillingClient.ReactivateSubscriptionAsync(10, Arg.Any<CancellationToken>()).Returns(reactivated);

        var subscriptionService = CreateSubscriptionService();

        var result = await subscriptionService.ReactivateAsync("buyer@test.com", 10);

        Assert.Equal(SubscriptionStates.Active, result.State);
        await _mockPublisher.Received(1).Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelAsync_WhenAlreadyCancelled_ThrowsConflict()
    {
        _mockBillingClient.GetSubscriptionAsync(10, Arg.Any<CancellationToken>())
            .Returns(SubscriptionWithState(SubscriptionStates.Canceled));

        var subscriptionService = CreateSubscriptionService();

        await Assert.ThrowsAsync<SubscriptionConflictException>(() =>
            subscriptionService.CancelAsync("buyer@test.com", 10, "no longer needed", endOfPeriod: false));
    }

    [Fact]
    public async Task CancelAsync_PassesEndOfPeriodFlagThroughToBillingClient()
    {
        var cancelled = SubscriptionWithState(SubscriptionStates.Active);
        _mockBillingClient.GetSubscriptionAsync(10, Arg.Any<CancellationToken>()).Returns(cancelled);
        _mockBillingClient.CancelSubscriptionAsync(10, "reason", true, Arg.Any<CancellationToken>())
            .Returns(cancelled);

        var subscriptionService = CreateSubscriptionService();

        await subscriptionService.CancelAsync("buyer@test.com", 10, "reason", endOfPeriod: true);

        await _mockBillingClient.Received(1).CancelSubscriptionAsync(10, "reason", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminCallerWithNullOwnerReference_BypassesOwnershipCheck()
    {
        var someoneElsesSubscription = SubscriptionWithState(SubscriptionStates.Active);
        _mockBillingClient.GetSubscriptionAsync(10, Arg.Any<CancellationToken>())
            .Returns(someoneElsesSubscription);
        _mockBillingClient.PauseSubscriptionAsync(10, Arg.Any<CancellationToken>())
            .Returns(SubscriptionWithState(SubscriptionStates.OnHold));

        var subscriptionService = CreateSubscriptionService();

        var result = await subscriptionService.PauseAsync(null, 10);

        Assert.Equal(SubscriptionStates.OnHold, result.State);
    }
}
