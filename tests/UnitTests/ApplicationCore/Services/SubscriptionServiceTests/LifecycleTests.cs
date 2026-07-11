using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class LifecycleTests
{
    private readonly IBillingClient _mockBillingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _mockPublisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _mockLogger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly SubscriptionBuilder _builder = new();

    private SubscriptionService CreateService() => new(_mockBillingClient, _mockPublisher, _mockLogger);

    [Fact]
    public async Task PauseSucceedsForAnActiveSubscription()
    {
        var active = _builder.Active();
        _mockBillingClient.GetSubscriptionAsync(SubscriptionBuilder.TestSubscriptionId, Arg.Any<CancellationToken>()).Returns(active);
        var paused = _builder.WithState("on_hold");
        _mockBillingClient.PauseSubscriptionAsync(SubscriptionBuilder.TestSubscriptionId, Arg.Any<CancellationToken>()).Returns(paused);

        var service = CreateService();
        var result = await service.PauseAsync(SubscriptionBuilder.TestBuyerId, false, SubscriptionBuilder.TestSubscriptionId);

        Assert.Equal("on_hold", result.State);
        await _mockPublisher.Received(1).Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PauseRejectsASubscriptionThatIsNotActive()
    {
        var canceled = _builder.WithState("canceled");
        _mockBillingClient.GetSubscriptionAsync(SubscriptionBuilder.TestSubscriptionId, Arg.Any<CancellationToken>()).Returns(canceled);

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => service.PauseAsync(SubscriptionBuilder.TestBuyerId, false, SubscriptionBuilder.TestSubscriptionId));
    }

    [Fact]
    public async Task ResumeRejectsASubscriptionThatIsNotPaused()
    {
        var active = _builder.Active();
        _mockBillingClient.GetSubscriptionAsync(SubscriptionBuilder.TestSubscriptionId, Arg.Any<CancellationToken>()).Returns(active);

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => service.ResumeAsync(SubscriptionBuilder.TestBuyerId, false, SubscriptionBuilder.TestSubscriptionId));
    }

    [Fact]
    public async Task CancelRejectsASubscriptionThatIsAlreadyCanceled()
    {
        var canceled = _builder.WithState("canceled");
        _mockBillingClient.GetSubscriptionAsync(SubscriptionBuilder.TestSubscriptionId, Arg.Any<CancellationToken>()).Returns(canceled);

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => service.CancelAsync(SubscriptionBuilder.TestBuyerId, false, SubscriptionBuilder.TestSubscriptionId, endOfPeriod: true, reason: null));
    }

    [Fact]
    public async Task ReactivateRejectsASubscriptionThatIsNotCanceled()
    {
        var active = _builder.Active();
        _mockBillingClient.GetSubscriptionAsync(SubscriptionBuilder.TestSubscriptionId, Arg.Any<CancellationToken>()).Returns(active);

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => service.ReactivateAsync(SubscriptionBuilder.TestBuyerId, false, SubscriptionBuilder.TestSubscriptionId));
    }

    [Fact]
    public async Task NonAdminCannotActOnSomeoneElsesSubscription()
    {
        var othersSubscription = _builder.WithBuyerId("someone-else@example.com");
        _mockBillingClient.GetSubscriptionAsync(SubscriptionBuilder.TestSubscriptionId, Arg.Any<CancellationToken>()).Returns(othersSubscription);

        var service = CreateService();

        await Assert.ThrowsAsync<Microsoft.eShopWeb.ApplicationCore.Exceptions.SubscriptionAccessDeniedException>(
            () => service.PauseAsync(SubscriptionBuilder.TestBuyerId, false, SubscriptionBuilder.TestSubscriptionId));
    }
}
