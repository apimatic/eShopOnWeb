using System.Collections.Generic;
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

public class SubscribeAsync
{
    private readonly IBillingClient _mockBillingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _mockPublisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _mockLogger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly SubscriptionBuilder _builder = new();

    private SubscriptionService CreateService() => new(_mockBillingClient, _mockPublisher, _mockLogger);

    [Fact]
    public async Task ReturnsExistingSubscription_WhenAlreadyActive_AndDoesNotCreateANewOne()
    {
        var existing = _builder.WithState(1, SubscriptionState.Active);
        _mockBillingClient.ListSubscriptionsForCustomerAsync(_builder.TestOwnerReference, default)
            .Returns(new List<Subscription> { existing });

        var service = CreateService();

        var result = await service.SubscribeAsync(_builder.TestOwnerReference, _builder.TestOwnerReference, _builder.TestProductHandle);

        Assert.Same(existing, result);
        await _mockBillingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), default);
        await _mockPublisher.DidNotReceive().Publish(Arg.Any<SubscriptionActivated>(), default);
    }

    [Fact]
    public async Task CreatesSubscriptionAndPublishesActivatedEvent_WhenNoneExists()
    {
        _mockBillingClient.ListSubscriptionsForCustomerAsync(_builder.TestOwnerReference, default)
            .Returns(new List<Subscription>());
        _mockBillingClient.ListPlansAsync(default)
            .Returns(new List<BillingPlan> { new(1, _builder.TestProductHandle, "Pro Plan", 29900, 1, "month") });

        var created = _builder.WithState(42, SubscriptionState.Active);
        _mockBillingClient.CreateSubscriptionAsync(_builder.TestOwnerReference, _builder.TestOwnerReference, _builder.TestProductHandle, default)
            .Returns(created);

        var service = CreateService();

        var result = await service.SubscribeAsync(_builder.TestOwnerReference, _builder.TestOwnerReference, _builder.TestProductHandle);

        Assert.Same(created, result);
        await _mockPublisher.Received().Publish(
            Arg.Is<SubscriptionActivated>(n => n.SubscriptionId == 42 && n.UserId == _builder.TestOwnerReference),
            default);
    }

    [Fact]
    public async Task ThrowsBillingConfigurationException_WhenProductHandleDoesNotResolve()
    {
        _mockBillingClient.ListSubscriptionsForCustomerAsync(_builder.TestOwnerReference, default)
            .Returns(new List<Subscription>());
        _mockBillingClient.ListPlansAsync(default)
            .Returns(new List<BillingPlan>());

        var service = CreateService();

        await Assert.ThrowsAsync<BillingConfigurationException>(() =>
            service.SubscribeAsync(_builder.TestOwnerReference, _builder.TestOwnerReference, "unknown-handle"));

        await _mockBillingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), default);
    }
}
