using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class SubscribeAsyncIdempotency : SubscriptionServiceFixture
{
    [Fact]
    public async Task SendsTheSameTokenForRetriesOfTheSameSubscribeIntent()
    {
        GivenPlanExists();
        GivenCustomerExists();
        GivenSubscriptions();
        Gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>()).Returns(Subscription());

        var service = CreateService();

        await service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle, IdempotencyKey: "click-1"));
        await service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle, IdempotencyKey: "click-1"));

        var tokens = Gateway.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(Gateway.CreateSubscriptionAsync))
            .Select(call => ((NewSubscription)call.GetArguments()[0]!).IdempotencyToken)
            .ToArray();

        Assert.Equal(2, tokens.Length);
        Assert.Equal(tokens[0], tokens[1]);
    }

    [Fact]
    public async Task SendsADifferentTokenForADifferentPlan()
    {
        GivenPlanExists();
        Gateway.FindPlanAsync("basic-plan", Arg.Any<CancellationToken>()).Returns(Plan("basic-plan", 2900));
        GivenCustomerExists();
        GivenSubscriptions();
        Gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>()).Returns(Subscription());

        var service = CreateService();

        await service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle));
        await service.SubscribeAsync(new SubscribeRequest(Subscriber, "basic-plan"));

        var tokens = Gateway.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(Gateway.CreateSubscriptionAsync))
            .Select(call => ((NewSubscription)call.GetArguments()[0]!).IdempotencyToken)
            .ToArray();

        Assert.Equal(2, tokens.Length);
        Assert.NotEqual(tokens[0], tokens[1]);
    }

    [Fact]
    public async Task DefaultsToACollectionMethodThatNeedsNoCardOnFile()
    {
        GivenPlanExists();
        GivenCustomerExists();
        GivenSubscriptions();
        Gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>()).Returns(Subscription());

        var service = CreateService();

        await service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle));

        await Gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewSubscription>(subscription =>
                subscription.PaymentCollectionMethod == Microsoft.eShopWeb.ApplicationCore.Services.SubscriptionService.DefaultPaymentCollectionMethod),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsTheWinningEnrollmentWhenTheProviderRejectsOurReplay()
    {
        GivenPlanExists();
        GivenCustomerExists();

        // Empty on the way in, then populated by the request that won the race.
        Gateway.ListSubscriptionsAsync(CustomerId, Arg.Any<CancellationToken>())
            .Returns(
                _ => new List<CustomerSubscription>(),
                _ => new List<CustomerSubscription> { Subscription(id: 99) });

        Gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns<CustomerSubscription>(_ => throw new ConcurrentSubscribeException("duplicate"));

        var service = CreateService();

        var result = await service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle));

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(99, result.Subscription.Id);
    }

    [Fact]
    public async Task SurfacesTheConflictWhenTheWinningEnrollmentCannotBeObserved()
    {
        GivenPlanExists();
        GivenCustomerExists();
        GivenSubscriptions();

        Gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns<CustomerSubscription>(_ => throw new ConcurrentSubscribeException("duplicate"));

        var service = CreateService();

        await Assert.ThrowsAsync<ConcurrentSubscribeException>(() =>
            service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle)));
    }

    [Fact]
    public async Task SendsTheSameTokenForAnUnkeyedRetryWithinTheRetryWindow()
    {
        GivenPlanExists();
        GivenCustomerExists();
        GivenSubscriptions();
        Gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>()).Returns(Subscription());

        var service = CreateService();

        await service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle));
        Clock.Now = Clock.Now.AddSeconds(30);
        await service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle));

        var tokens = CreateSubscriptionTokens();

        Assert.Equal(tokens[0], tokens[1]);
    }

    /// <summary>
    /// A shopper who cancels must be able to enroll again. A token that never changed would keep
    /// the provider rejecting their request as a duplicate long after the fact.
    /// </summary>
    [Fact]
    public async Task SendsAFreshTokenOnceTheRetryWindowHasPassed()
    {
        GivenPlanExists();
        GivenCustomerExists();
        GivenSubscriptions();
        Gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>()).Returns(Subscription());

        var service = CreateService();

        await service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle));
        Clock.Now = Clock.Now.Add(Microsoft.eShopWeb.ApplicationCore.Services.SubscriptionService.RetryWindow);
        await service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle));

        var tokens = CreateSubscriptionTokens();

        Assert.NotEqual(tokens[0], tokens[1]);
    }

    [Fact]
    public async Task DistinguishesTwoDeliberateAttemptsWithDifferentKeys()
    {
        GivenPlanExists();
        GivenCustomerExists();
        GivenSubscriptions();
        Gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>()).Returns(Subscription());

        var service = CreateService();

        await service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle, IdempotencyKey: "click-1"));
        await service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle, IdempotencyKey: "click-2"));

        var tokens = CreateSubscriptionTokens();

        Assert.NotEqual(tokens[0], tokens[1]);
    }

    private string[] CreateSubscriptionTokens() =>
        Gateway.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(Gateway.CreateSubscriptionAsync))
            .Select(call => ((NewSubscription)call.GetArguments()[0]!).IdempotencyToken)
            .ToArray();
}
