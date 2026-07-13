using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class ChangeLifecycleStateAsync
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPlanChangePreviewCache _previewCache = Substitute.For<IPlanChangePreviewCache>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private SubscriptionService CreateSut() => new(_billingClient, _previewCache, _publisher, _logger);

    private static BillingSubscription Subscription(string state) =>
        new(1, 10, "buyer@example.com", 7111477, "eshop-pro", "Pro Plan", state, 29900, null, null, null);

    [Fact]
    public async Task RejectsResume_WhenSubscriptionIsNotPaused_AndMakesNoProviderCall()
    {
        _billingClient.GetSubscriptionAsync(1).Returns(Subscription("active"));
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidSubscriptionStateException>(() =>
            sut.ChangeLifecycleStateAsync(1, "buyer@example.com", isAdmin: false, SubscriptionLifecycleAction.Resume, endOfPeriod: false, reason: null));

        await _billingClient.DidNotReceiveWithAnyArgs().ResumeAsync(default);
    }

    [Fact]
    public async Task RejectsPause_WhenSubscriptionIsAlreadyCancelled()
    {
        _billingClient.GetSubscriptionAsync(1).Returns(Subscription("canceled"));
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidSubscriptionStateException>(() =>
            sut.ChangeLifecycleStateAsync(1, "buyer@example.com", isAdmin: false, SubscriptionLifecycleAction.Pause, endOfPeriod: false, reason: null));

        await _billingClient.DidNotReceiveWithAnyArgs().PauseAsync(default);
    }

    [Fact]
    public async Task RejectsReactivate_WhenSubscriptionIsAlreadyActive()
    {
        _billingClient.GetSubscriptionAsync(1).Returns(Subscription("active"));
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidSubscriptionStateException>(() =>
            sut.ChangeLifecycleStateAsync(1, "buyer@example.com", isAdmin: false, SubscriptionLifecycleAction.Reactivate, endOfPeriod: false, reason: null));
    }

    [Fact]
    public async Task PausesActiveSubscription_AndPublishesStateChangedNotification()
    {
        _billingClient.GetSubscriptionAsync(1).Returns(Subscription("active"));
        _billingClient.PauseAsync(1).Returns(Subscription("on_hold"));
        var sut = CreateSut();

        var result = await sut.ChangeLifecycleStateAsync(1, "buyer@example.com", isAdmin: false, SubscriptionLifecycleAction.Pause, endOfPeriod: false, reason: null);

        Assert.Equal("on_hold", result.State);
        await _publisher.Received(1).Publish(
            Arg.Is<object>(n => n.GetType() == typeof(Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.SubscriptionStateChanged)
                && ((Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.SubscriptionStateChanged)n).PreviousState == "active"
                && ((Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.SubscriptionStateChanged)n).NewState == "on_hold"),
            Arg.Any<System.Threading.CancellationToken>());
    }

    [Fact]
    public async Task AdminCanActOnAnotherUsersSubscription()
    {
        _billingClient.GetSubscriptionAsync(1).Returns(Subscription("active"));
        _billingClient.PauseAsync(1).Returns(Subscription("on_hold"));
        var sut = CreateSut();

        var result = await sut.ChangeLifecycleStateAsync(1, "admin@example.com", isAdmin: true, SubscriptionLifecycleAction.Pause, endOfPeriod: false, reason: null);

        Assert.Equal("on_hold", result.State);
    }

    [Fact]
    public async Task NonAdminCannotActOnAnotherUsersSubscription()
    {
        _billingClient.GetSubscriptionAsync(1).Returns(Subscription("active"));
        var sut = CreateSut();

        await Assert.ThrowsAsync<SubscriptionAccessDeniedException>(() =>
            sut.ChangeLifecycleStateAsync(1, "not-the-owner@example.com", isAdmin: false, SubscriptionLifecycleAction.Pause, endOfPeriod: false, reason: null));
    }
}
