using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.ApplicationCore.Services.SubscriptionServiceTests;

public class Subscribe
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly SubscriptionService _subscriptionService;

    public Subscribe()
    {
        _subscriptionService = new SubscriptionService(_billingClient, _publisher,
            Substitute.For<IAppLogger<SubscriptionService>>(),
            new SubscriptionSettings { ProductFamilyHandle = "eshop-subscribe", MeteredComponentHandle = "api-call" });

        _billingClient.GetPlanByHandleAsync("eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Plan());
    }

    [Fact]
    public async Task CreatesTheProviderCustomerWhenTheUserHasNoneYet()
    {
        _billingClient.FindCustomerByReferenceAsync(SubscriptionBuilder.TEST_USER_REFERENCE, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);
        _billingClient.CreateCustomerAsync(SubscriptionBuilder.TEST_USER_REFERENCE,
            SubscriptionBuilder.TEST_USER_REFERENCE, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Customer());
        _billingClient.ListSubscriptionsForCustomerAsync(SubscriptionBuilder.TEST_CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _billingClient.CreateSubscriptionAsync(SubscriptionBuilder.TEST_USER_REFERENCE, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription());

        var subscription = await _subscriptionService.SubscribeAsync(SubscriptionBuilder.TEST_USER_REFERENCE, "eshop-pro");

        Assert.Equal(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, subscription.Id);
        await _billingClient.Received(1).CreateCustomerAsync(SubscriptionBuilder.TEST_USER_REFERENCE,
            SubscriptionBuilder.TEST_USER_REFERENCE, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReusesTheExistingProviderCustomerSoRepeatedSubscribesAreIdempotent()
    {
        GivenAnExistingCustomerWith(Array.Empty<CustomerSubscription>());
        _billingClient.CreateSubscriptionAsync(SubscriptionBuilder.TEST_USER_REFERENCE, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription());

        await _subscriptionService.SubscribeAsync(SubscriptionBuilder.TEST_USER_REFERENCE, "eshop-pro");

        await _billingClient.DidNotReceive().CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsTheExistingActiveSubscriptionRatherThanEnrollingTwice()
    {
        var existing = SubscriptionBuilder.Subscription(SubscriptionState.Active);
        GivenAnExistingCustomerWith(new[] { existing });

        var subscription = await _subscriptionService.SubscribeAsync(SubscriptionBuilder.TEST_USER_REFERENCE, "eshop-pro");

        Assert.Same(existing, subscription);
        await _billingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrollsAgainWhenEveryPreviousSubscriptionIsCancelled()
    {
        GivenAnExistingCustomerWith(new[] { SubscriptionBuilder.Subscription(SubscriptionState.Canceled) });
        _billingClient.CreateSubscriptionAsync(SubscriptionBuilder.TEST_USER_REFERENCE, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(id: 15236999));

        var subscription = await _subscriptionService.SubscribeAsync(SubscriptionBuilder.TEST_USER_REFERENCE, "eshop-pro");

        Assert.Equal(15236999, subscription.Id);
    }

    [Fact]
    public async Task RefusesToEnrollAgainstAPlanHandleThatDoesNotResolve()
    {
        _billingClient.GetPlanByHandleAsync("gone-plan", Arg.Any<CancellationToken>())
            .Returns((SubscriptionPlan?)null);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _subscriptionService.SubscribeAsync(SubscriptionBuilder.TEST_USER_REFERENCE, "gone-plan"));

        Assert.Contains("gone-plan", exception.Message);

        // Nothing is created against a guessed plan.
        await _billingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _billingClient.DidNotReceive().CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnnouncesTheActivationInProcess()
    {
        GivenAnExistingCustomerWith(Array.Empty<CustomerSubscription>());
        _billingClient.CreateSubscriptionAsync(SubscriptionBuilder.TEST_USER_REFERENCE, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription());

        await _subscriptionService.SubscribeAsync(SubscriptionBuilder.TEST_USER_REFERENCE, "eshop-pro");

        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionActivated>(activated =>
                activated.SubscriptionId == SubscriptionBuilder.TEST_SUBSCRIPTION_ID
                && activated.PlanHandle == "eshop-pro"
                && activated.PlanPrice == 299.00m
                && activated.CustomerReference == SubscriptionBuilder.TEST_USER_REFERENCE),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeepsTheSubscriptionWhenAHandlerFailsBecauseEventingIsBestEffort()
    {
        GivenAnExistingCustomerWith(Array.Empty<CustomerSubscription>());
        _billingClient.CreateSubscriptionAsync(SubscriptionBuilder.TEST_USER_REFERENCE, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription());
        _publisher.Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("handler blew up"));

        var subscription = await _subscriptionService.SubscribeAsync(SubscriptionBuilder.TEST_USER_REFERENCE, "eshop-pro");

        Assert.Equal(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, subscription.Id);
    }

    [Fact]
    public async Task ReturnsNoSubscriptionsForAUserWithNoProviderCustomer()
    {
        _billingClient.FindCustomerByReferenceAsync(SubscriptionBuilder.TEST_USER_REFERENCE, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        var subscriptions = await _subscriptionService.GetMySubscriptionsAsync(SubscriptionBuilder.TEST_USER_REFERENCE);

        Assert.Empty(subscriptions);
        await _billingClient.DidNotReceive().ListSubscriptionsForCustomerAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private void GivenAnExistingCustomerWith(IReadOnlyCollection<CustomerSubscription> subscriptions)
    {
        _billingClient.FindCustomerByReferenceAsync(SubscriptionBuilder.TEST_USER_REFERENCE, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Customer());
        _billingClient.ListSubscriptionsForCustomerAsync(SubscriptionBuilder.TEST_CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(subscriptions);
    }
}
