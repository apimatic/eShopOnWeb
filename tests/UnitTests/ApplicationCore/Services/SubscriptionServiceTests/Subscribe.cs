using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class Subscribe
{
    private readonly IBillingClient _mockBillingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _mockPublisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _mockLogger = Substitute.For<IAppLogger<SubscriptionService>>();

    private SubscriptionService CreateSubscriptionService() =>
        new(_mockBillingClient, _mockPublisher, _mockLogger);

    [Fact]
    public async Task WhenNoExistingSubscription_CreatesNewSubscriptionAndPublishesNotification()
    {
        var customer = new BillingCustomer(1, "buyer@test.com", "buyer@test.com");
        var newSubscription = new CustomerSubscription(10, "buyer@test.com", SubscriptionStates.Active,
            "eshop-pro", "Pro Plan", 29900, null, null, false, null, 0);

        _mockBillingClient.EnsureCustomerAsync("buyer@test.com", "buyer@test.com", Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(customer);
        _mockBillingClient.ListCustomerSubscriptionsAsync(1, Arg.Any<CancellationToken>())
            .Returns(new List<CustomerSubscription>());
        _mockBillingClient.CreateSubscriptionAsync(1, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(newSubscription);

        var subscriptionService = CreateSubscriptionService();

        var result = await subscriptionService.SubscribeAsync("buyer@test.com", "buyer@test.com", "eshop-pro");

        Assert.Equal(10, result.Id);
        await _mockBillingClient.Received(1).CreateSubscriptionAsync(1, "eshop-pro", Arg.Any<CancellationToken>());
        await _mockPublisher.Received(1).Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenCustomerAlreadyHasALiveSubscription_ReturnsExistingSubscriptionWithoutCreatingANewOne()
    {
        var customer = new BillingCustomer(1, "buyer@test.com", "buyer@test.com");
        var existingSubscription = new CustomerSubscription(5, "buyer@test.com", SubscriptionStates.Trialing,
            "basic-plan", "Basic Plan", 2900, null, null, false, null, 0);

        _mockBillingClient.EnsureCustomerAsync("buyer@test.com", "buyer@test.com", Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(customer);
        _mockBillingClient.ListCustomerSubscriptionsAsync(1, Arg.Any<CancellationToken>())
            .Returns(new List<CustomerSubscription> { existingSubscription });

        var subscriptionService = CreateSubscriptionService();

        var result = await subscriptionService.SubscribeAsync("buyer@test.com", "buyer@test.com", "eshop-pro");

        Assert.Equal(5, result.Id);
        await _mockBillingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenCustomerOnlyHasACancelledSubscription_StillCreatesANewOne()
    {
        var customer = new BillingCustomer(1, "buyer@test.com", "buyer@test.com");
        var cancelledSubscription = new CustomerSubscription(5, "buyer@test.com", SubscriptionStates.Canceled,
            "basic-plan", "Basic Plan", 2900, null, null, false, null, 0);
        var newSubscription = new CustomerSubscription(11, "buyer@test.com", SubscriptionStates.Active,
            "eshop-pro", "Pro Plan", 29900, null, null, false, null, 0);

        _mockBillingClient.EnsureCustomerAsync("buyer@test.com", "buyer@test.com", Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(customer);
        _mockBillingClient.ListCustomerSubscriptionsAsync(1, Arg.Any<CancellationToken>())
            .Returns(new List<CustomerSubscription> { cancelledSubscription });
        _mockBillingClient.CreateSubscriptionAsync(1, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(newSubscription);

        var subscriptionService = CreateSubscriptionService();

        var result = await subscriptionService.SubscribeAsync("buyer@test.com", "buyer@test.com", "eshop-pro");

        Assert.Equal(11, result.Id);
    }

    [Fact]
    public async Task WhenNotificationPublishThrows_SubscriptionStillStands()
    {
        var customer = new BillingCustomer(1, "buyer@test.com", "buyer@test.com");
        var newSubscription = new CustomerSubscription(10, "buyer@test.com", SubscriptionStates.Active,
            "eshop-pro", "Pro Plan", 29900, null, null, false, null, 0);

        _mockBillingClient.EnsureCustomerAsync("buyer@test.com", "buyer@test.com", Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(customer);
        _mockBillingClient.ListCustomerSubscriptionsAsync(1, Arg.Any<CancellationToken>())
            .Returns(new List<CustomerSubscription>());
        _mockBillingClient.CreateSubscriptionAsync(1, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(newSubscription);
        _mockPublisher.Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("handler exploded"));

        var subscriptionService = CreateSubscriptionService();

        var result = await subscriptionService.SubscribeAsync("buyer@test.com", "buyer@test.com", "eshop-pro");

        Assert.Equal(10, result.Id);
    }
}
