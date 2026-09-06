using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class Subscribe
{
    private readonly ISubscriptionBillingGateway _gateway = Substitute.For<ISubscriptionBillingGateway>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly SubscriberIdentity _subscriber = SubscriptionBuilder.Subscriber();

    private SubscriptionService CreateService() => new(_gateway, _logger);

    public Subscribe()
    {
        _gateway.FindPlanAsync(SubscriptionBuilder.ProPlanHandle, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Plan());
    }

    [Fact]
    public async Task CreatesTheBillingCustomerOnFirstSubscribe()
    {
        _gateway.FindCustomerByReferenceAsync(_subscriber.BillingReference, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);
        _gateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Customer());
        _gateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription());

        var result = await CreateService().SubscribeAsync(_subscriber, SubscriptionBuilder.ProPlanHandle);

        Assert.True(result.Created);
        await _gateway.Received(1).CreateCustomerAsync(
            Arg.Is<NewBillingCustomer>(c =>
                c.Reference == "eshop:demouser@microsoft.com" && c.Email == "demouser@microsoft.com"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UsesAReferenceDerivedFromTheSubscriberAndPlan()
    {
        GivenExistingCustomerWithSubscriptions();
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription());

        await CreateService().SubscribeAsync(_subscriber, SubscriptionBuilder.ProPlanHandle);

        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewSubscription>(s =>
                s.CustomerId == 42 &&
                s.PlanHandle == SubscriptionBuilder.ProPlanHandle &&
                s.Reference == "eshop:demouser@microsoft.com:eshop-pro"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotCreateASecondCustomerWhenOneAlreadyExists()
    {
        GivenExistingCustomerWithSubscriptions();
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription());

        await CreateService().SubscribeAsync(_subscriber, SubscriptionBuilder.ProPlanHandle);

        await _gateway.DidNotReceive().CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsTheExistingSubscriptionWhenAlreadyOnThePlan()
    {
        var existing = SubscriptionBuilder.Subscription(id: 555, reference: "eshop:demouser@microsoft.com:eshop-pro");
        GivenExistingCustomerWithSubscriptions(existing);

        var result = await CreateService().SubscribeAsync(_subscriber, SubscriptionBuilder.ProPlanHandle);

        Assert.False(result.Created);
        Assert.Equal(555, result.Subscription.Id);
        await _gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribesAgainWhenThePreviousSubscriptionEndedItsLife()
    {
        var cancelled = SubscriptionBuilder.Subscription(id: 555, state: SubscriptionStates.Canceled,
            reference: "eshop:demouser@microsoft.com:eshop-pro");
        GivenExistingCustomerWithSubscriptions(cancelled);
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(id: 556));

        var result = await CreateService().SubscribeAsync(_subscriber, SubscriptionBuilder.ProPlanHandle);

        Assert.True(result.Created);
        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewSubscription>(s => s.Reference == "eshop:demouser@microsoft.com:eshop-pro:2"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolvesAConcurrentSignupByReadingBackTheWinner()
    {
        GivenExistingCustomerWithSubscriptions();
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Throws(new BillingReferenceConflictException("eshop:demouser@microsoft.com:eshop-pro"));
        _gateway.FindSubscriptionByReferenceAsync("eshop:demouser@microsoft.com:eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(id: 777));

        var result = await CreateService().SubscribeAsync(_subscriber, SubscriptionBuilder.ProPlanHandle);

        Assert.False(result.Created);
        Assert.Equal(777, result.Subscription.Id);
    }

    [Fact]
    public async Task RethrowsWhenAConflictCannotBeResolved()
    {
        GivenExistingCustomerWithSubscriptions();
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Throws(new BillingReferenceConflictException("eshop:demouser@microsoft.com:eshop-pro"));
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CustomerSubscription?)null);

        await Assert.ThrowsAsync<BillingReferenceConflictException>(() =>
            CreateService().SubscribeAsync(_subscriber, SubscriptionBuilder.ProPlanHandle));
    }

    [Fact]
    public async Task ResolvesAConcurrentCustomerCreationByReadingBackTheWinner()
    {
        _gateway.FindCustomerByReferenceAsync(_subscriber.BillingReference, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null, SubscriptionBuilder.Customer());
        _gateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>())
            .Throws(new BillingReferenceConflictException(_subscriber.BillingReference));
        _gateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription());

        var result = await CreateService().SubscribeAsync(_subscriber, SubscriptionBuilder.ProPlanHandle);

        Assert.True(result.Created);
    }

    [Fact]
    public async Task ReplaysTheSameSubscriptionForARepeatedIdempotencyKey()
    {
        var existing = SubscriptionBuilder.Subscription(id: 999,
            reference: "eshop:demouser@microsoft.com:key:click-1");
        GivenExistingCustomerWithSubscriptions(existing);

        var result = await CreateService().SubscribeAsync(_subscriber, SubscriptionBuilder.ProPlanHandle, "Click 1");

        Assert.False(result.Created);
        Assert.Equal(999, result.Subscription.Id);
        await _gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HonoursTheIdempotencyKeyEvenWhenTheShopperIsAlreadyOnThePlan()
    {
        var onPlan = SubscriptionBuilder.Subscription(id: 111, reference: "eshop:demouser@microsoft.com:eshop-pro");
        GivenExistingCustomerWithSubscriptions(onPlan);
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(id: 222));

        var result = await CreateService().SubscribeAsync(_subscriber, SubscriptionBuilder.ProPlanHandle, "click-2");

        Assert.True(result.Created);
        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewSubscription>(s => s.Reference == "eshop:demouser@microsoft.com:key:click-2"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAPlanTheProductFamilyDoesNotOffer()
    {
        _gateway.FindPlanAsync("nope", Arg.Any<CancellationToken>()).Returns((SubscriptionPlan?)null);

        var exception = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            CreateService().SubscribeAsync(_subscriber, "nope"));

        Assert.Equal("nope", exception.PlanHandle);
    }

    [Fact]
    public async Task RejectsAPlanThatDemandsAStoredPaymentMethod()
    {
        _gateway.FindPlanAsync("card-only", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Plan("card-only", requiresPaymentMethod: true));

        await Assert.ThrowsAsync<PaymentMethodRequiredException>(() =>
            CreateService().SubscribeAsync(_subscriber, "card-only"));

        await _gateway.DidNotReceive().CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>());
    }

    private void GivenExistingCustomerWithSubscriptions(params CustomerSubscription[] subscriptions)
    {
        _gateway.FindCustomerByReferenceAsync(_subscriber.BillingReference, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Customer());
        _gateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(subscriptions);
    }
}
