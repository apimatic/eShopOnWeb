using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class Subscribe : SubscriptionServiceFixture
{
    public Subscribe()
    {
        GivenPlans(Plan(ProPlanHandle, 29900, "pro-price-point"), Plan(BasicPlanHandle, 2900));
    }

    [Fact]
    public async Task CreatesTheBillingCustomerWhenTheShopperHasNoneYet()
    {
        GivenExistingCustomer(null);
        GivenSubscriptions();
        MockGateway.CreateCustomerAsync(Arg.Any<NewCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(Customer());
        MockGateway.CreateSubscriptionAsync(Arg.Any<NewSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, ProPlanHandle));

        var result = await CreateService().SubscribeAsync(Subscriber);

        Assert.True(result.Created);
        Assert.True(result.CustomerCreated);
        await MockGateway.Received(1).CreateCustomerAsync(
            Arg.Is<NewCustomerRequest>(r => r.Reference == Subscriber.CustomerReference),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReusesTheBillingCustomerWhenTheShopperAlreadyHasOne()
    {
        GivenExistingCustomer(Customer());
        GivenSubscriptions();
        MockGateway.CreateSubscriptionAsync(Arg.Any<NewSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, ProPlanHandle));

        var result = await CreateService().SubscribeAsync(Subscriber);

        Assert.True(result.Created);
        Assert.False(result.CustomerCreated);
        await MockGateway.DidNotReceive().CreateCustomerAsync(Arg.Any<NewCustomerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadsTheCustomerBackWhenAConcurrentRequestCreatedItFirst()
    {
        // First lookup misses, the create loses the race, the second lookup finds the winner's customer.
        MockGateway.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => null, _ => Customer());
        MockGateway.CreateCustomerAsync(Arg.Any<NewCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns<BillingCustomer>(_ => throw new DuplicateBillingReferenceException(Subscriber.CustomerReference));
        GivenSubscriptions();
        MockGateway.CreateSubscriptionAsync(Arg.Any<NewSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, ProPlanHandle));

        var result = await CreateService().SubscribeAsync(Subscriber);

        Assert.True(result.Created);
        Assert.False(result.CustomerCreated);
    }

    [Fact]
    public async Task ReturnsTheExistingSubscriptionInsteadOfEnrollingTwice()
    {
        GivenExistingCustomer(Customer());
        GivenSubscriptions(Subscription(7, ProPlanHandle));

        var result = await CreateService().SubscribeAsync(Subscriber, ProPlanHandle);

        Assert.False(result.Created);
        Assert.Equal(7, result.Subscription.Id);
        await MockGateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<NewSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(SubscriptionStates.PastDue)]
    [InlineData(SubscriptionStates.OnHold)]
    [InlineData(SubscriptionStates.Suspended)]
    [InlineData(SubscriptionStates.Unpaid)]
    public async Task TreatsARecoverableProblemStateAsAnExistingEnrollment(string state)
    {
        GivenExistingCustomer(Customer());
        GivenSubscriptions(Subscription(7, ProPlanHandle, state));

        var result = await CreateService().SubscribeAsync(Subscriber, ProPlanHandle);

        Assert.False(result.Created);
        Assert.Equal(7, result.Subscription.Id);
    }

    [Theory]
    [InlineData(SubscriptionStates.Canceled)]
    [InlineData(SubscriptionStates.Expired)]
    [InlineData(SubscriptionStates.FailedToCreate)]
    [InlineData(SubscriptionStates.TrialEnded)]
    public async Task EnrollsAgainWhenTheOnlyPriorSubscriptionHasEnded(string state)
    {
        GivenExistingCustomer(Customer());
        GivenSubscriptions(Subscription(7, ProPlanHandle, state));
        MockGateway.CreateSubscriptionAsync(Arg.Any<NewSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(8, ProPlanHandle));

        var result = await CreateService().SubscribeAsync(Subscriber, ProPlanHandle);

        Assert.True(result.Created);
        Assert.Equal(8, result.Subscription.Id);
    }

    [Fact]
    public async Task EnrollsInASecondPlanTheShopperDoesNotHaveYet()
    {
        GivenExistingCustomer(Customer());
        GivenSubscriptions(Subscription(7, ProPlanHandle));
        MockGateway.CreateSubscriptionAsync(Arg.Any<NewSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(8, BasicPlanHandle));

        var result = await CreateService().SubscribeAsync(Subscriber, BasicPlanHandle);

        Assert.True(result.Created);
        Assert.Equal(8, result.Subscription.Id);
    }

    [Fact]
    public async Task SendsThePlansPricePointSoTheShopperGetsThePriceTheyWereShown()
    {
        GivenExistingCustomer(Customer());
        GivenSubscriptions();
        MockGateway.CreateSubscriptionAsync(Arg.Any<NewSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, ProPlanHandle));

        await CreateService().SubscribeAsync(Subscriber, ProPlanHandle);

        await MockGateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewSubscriptionRequest>(r =>
                r.PlanHandle == ProPlanHandle &&
                r.PricePointHandle == "pro-price-point" &&
                r.CustomerId == CustomerId &&
                r.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UsesTheCallersIdempotencyKeyAsTheUniquenessToken()
    {
        GivenExistingCustomer(Customer());
        GivenSubscriptions();
        MockGateway.CreateSubscriptionAsync(Arg.Any<NewSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, ProPlanHandle));

        await CreateService().SubscribeAsync(Subscriber, ProPlanHandle, idempotencyKey: "caller-key");

        await MockGateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewSubscriptionRequest>(r => r.UniquenessToken == "caller-key"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivesEachAttemptItsOwnTokenWhenTheCallerSuppliesNone()
    {
        GivenExistingCustomer(Customer());
        GivenSubscriptions();
        var tokens = new List<string>();
        MockGateway.CreateSubscriptionAsync(Arg.Any<NewSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                tokens.Add(call.ArgAt<NewSubscriptionRequest>(0).UniquenessToken);
                return Subscription(1, ProPlanHandle);
            });

        var service = CreateService();
        await service.SubscribeAsync(Subscriber, ProPlanHandle);
        await service.SubscribeAsync(Subscriber, ProPlanHandle);

        // A shared token would lock the shopper out of a genuine retry for the dedupe window.
        Assert.Equal(2, tokens.Distinct().Count());
    }

    [Fact]
    public async Task GivesEachAttemptItsOwnSubscriptionReferenceBecauseAFailedAttemptConsumesIt()
    {
        GivenExistingCustomer(Customer());
        GivenSubscriptions();
        var references = new List<string?>();
        MockGateway.CreateSubscriptionAsync(Arg.Any<NewSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                references.Add(call.ArgAt<NewSubscriptionRequest>(0).Reference);
                return Subscription(1, ProPlanHandle);
            });

        var service = CreateService();
        await service.SubscribeAsync(Subscriber, ProPlanHandle);
        await service.SubscribeAsync(Subscriber, ProPlanHandle);

        Assert.Equal(2, references.Distinct().Count());
        Assert.All(references, r => Assert.StartsWith(Subscriber.CustomerReference, r!));
    }

    [Fact]
    public async Task RecoversTheFirstAttemptsSubscriptionWhenTheBillingSystemRejectsADuplicate()
    {
        GivenExistingCustomer(Customer());
        // Empty before the create, populated afterwards - what a lost-response retry actually sees.
        MockGateway.ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => new List<CustomerSubscription>(),
                     _ => new List<CustomerSubscription> { Subscription(9, ProPlanHandle) });
        MockGateway.CreateSubscriptionAsync(Arg.Any<NewSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns<CustomerSubscription>(_ => throw new DuplicateBillingSubmissionException("token"));

        var result = await CreateService().SubscribeAsync(Subscriber, ProPlanHandle, "token");

        Assert.False(result.Created);
        Assert.Equal(9, result.Subscription.Id);
    }

    [Fact]
    public async Task ReportsAConflictWhenADuplicateSubmissionLeftNothingBehind()
    {
        GivenExistingCustomer(Customer());
        GivenSubscriptions();
        MockGateway.CreateSubscriptionAsync(Arg.Any<NewSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns<CustomerSubscription>(_ => throw new DuplicateBillingSubmissionException("token"));

        await Assert.ThrowsAsync<SubscriptionConflictException>(
            () => CreateService().SubscribeAsync(Subscriber, ProPlanHandle, "token"));
    }

    [Fact]
    public async Task RejectsAPlanThatIsNotOnOffer()
    {
        GivenExistingCustomer(Customer());

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(Subscriber, "not-a-plan"));
    }

    [Fact]
    public async Task FallsBackToTheConfiguredDefaultPlanWhenTheCallerNamesNone()
    {
        GivenExistingCustomer(Customer());
        GivenSubscriptions();
        MockGateway.CreateSubscriptionAsync(Arg.Any<NewSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, BasicPlanHandle));

        await CreateService(defaultPlanHandle: BasicPlanHandle).SubscribeAsync(Subscriber);

        await MockGateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewSubscriptionRequest>(r => r.PlanHandle == BasicPlanHandle), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MakesTheCallerChooseWhenThereIsNoDefaultAndSeveralPlans()
    {
        GivenExistingCustomer(Customer());

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService(defaultPlanHandle: null).SubscribeAsync(Subscriber));
    }

    [Fact]
    public async Task UsesTheOnlyPlanOnOfferWhenThereIsNoDefault()
    {
        GivenPlans(Plan(ProPlanHandle));
        GivenExistingCustomer(Customer());
        GivenSubscriptions();
        MockGateway.CreateSubscriptionAsync(Arg.Any<NewSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, ProPlanHandle));

        var result = await CreateService(defaultPlanHandle: null).SubscribeAsync(Subscriber);

        Assert.True(result.Created);
    }
}
