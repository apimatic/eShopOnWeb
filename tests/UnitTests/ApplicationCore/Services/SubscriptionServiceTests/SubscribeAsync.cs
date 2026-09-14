using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class SubscribeAsync : SubscriptionServiceFixture
{
    [Fact]
    public async Task ThrowsWhenThePlanIsNotOffered()
    {
        Gateway.FindPlanAsync("nope", Arg.Any<CancellationToken>()).Returns((SubscriptionPlan?)null);

        var service = CreateService();

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            service.SubscribeAsync(new SubscribeRequest(Subscriber, "nope")));

        await Gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatesTheBillingCustomerWhenThereIsNoneYet()
    {
        GivenPlanExists();
        Gateway.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        Gateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>()).Returns(Customer());
        GivenSubscriptions();
        Gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>()).Returns(Subscription());

        var service = CreateService();

        var result = await service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle));

        Assert.False(result.AlreadySubscribed);
        await Gateway.Received(1).CreateCustomerAsync(
            Arg.Is<NewBillingCustomer>(customer => customer.Reference == BillingReferences.ForUser(UserName)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReusesTheBillingCustomerWhenOneAlreadyExists()
    {
        GivenPlanExists();
        GivenCustomerExists();
        GivenSubscriptions();
        Gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>()).Returns(Subscription());

        var service = CreateService();

        await service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle));

        await Gateway.DidNotReceive().CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsTheExistingSubscriptionInsteadOfEnrollingTwice()
    {
        GivenPlanExists();
        GivenCustomerExists();
        GivenSubscriptions(Subscription(id: 77));

        var service = CreateService();

        var result = await service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle));

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(77, result.Subscription.Id);
        await Gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    [InlineData("trial_ended")]
    [InlineData("failed_to_create")]
    public async Task EnrollsAgainWhenTheOnlySubscriptionIsInATerminalState(string state)
    {
        GivenPlanExists();
        GivenCustomerExists();
        GivenSubscriptions(Subscription(id: 77, state: state));
        Gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>()).Returns(Subscription(id: 78));

        var service = CreateService();

        var result = await service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle));

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(78, result.Subscription.Id);
    }

    [Fact]
    public async Task IgnoresLiveSubscriptionsToOtherPlans()
    {
        GivenPlanExists();
        GivenCustomerExists();
        GivenSubscriptions(Subscription(id: 77, planHandle: "basic-plan"));
        Gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>()).Returns(Subscription(id: 78));

        var service = CreateService();

        var result = await service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle));

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(78, result.Subscription.Id);
    }
}
