using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class SubscribeAsync
{
    private const string UserName = "shopper@example.com";
    private const string CustomerReference = "eshoponweb-shopper@example.com";
    private const long CustomerId = 4242;

    private readonly IBillingGateway _gateway = Substitute.For<IBillingGateway>();
    private readonly ISubscriberDirectory _directory = Substitute.For<ISubscriberDirectory>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly SubscriptionOptions _options = new() { CustomerReferencePrefix = "eshoponweb" };
    private readonly SubscriptionPlan _plan = new SubscriptionPlanBuilder().Build();

    public SubscribeAsync()
    {
        _directory.FindByUserNameAsync(UserName, Arg.Any<CancellationToken>())
            .Returns(new SubscriberContact("user-1", UserName, UserName));

        _gateway.ListPlansAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan> { _plan });

        _gateway.GetSiteAsync(Arg.Any<CancellationToken>())
            .Returns(new BillingSiteInfo(1, "Test", "test", "USD", true, "automatic", true));
    }

    private SubscriptionService CreateService() =>
        new(_gateway, _directory, new KeyedAsyncLock(), _options, _logger);

    [Fact]
    public async Task CreatesCustomerAndSubscriptionForFirstTimeSubscriber()
    {
        _gateway.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        _gateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(CustomerId, CustomerReference, "Shopper", "Example", UserName));
        _gateway.ListCustomerSubscriptionsAsync(CustomerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().WithId(77).Build());

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(UserName, "eshop-pro"));

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(77, result.Subscription.Id);
        Assert.Equal(CustomerId, result.Customer.Id);
        await _gateway.Received(1).CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>());
        await _gateway.Received(1).CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReusesExistingCustomerInsteadOfCreatingASecondOne()
    {
        GivenExistingCustomer();
        _gateway.ListCustomerSubscriptionsAsync(CustomerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().Build());

        await CreateService().SubscribeAsync(new SubscribeRequest(UserName, "eshop-pro"));

        await _gateway.DidNotReceive().CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsTheExistingSubscriptionWhenTheShopperAlreadyHoldsThePlan()
    {
        GivenExistingCustomer();
        var existing = new SubscriptionBuilder().WithId(55).WithPlanHandle("eshop-pro").Build();
        _gateway.ListCustomerSubscriptionsAsync(CustomerId, Arg.Any<CancellationToken>())
            .Returns(new[] { existing });

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(UserName, "eshop-pro"));

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.Equal(55, result.Subscription.Id);
        await _gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribesAgainWhenTheOnlyPriorSubscriptionForThePlanWasCanceled()
    {
        GivenExistingCustomer();
        var canceled = new SubscriptionBuilder()
            .WithId(55)
            .WithState(SubscriptionState.Canceled, "canceled")
            .Build();
        _gateway.ListCustomerSubscriptionsAsync(CustomerId, Arg.Any<CancellationToken>())
            .Returns(new[] { canceled });
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().WithId(56).Build());

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(UserName, "eshop-pro"));

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(56, result.Subscription.Id);
    }

    [Fact]
    public async Task ReturnsTheOriginalSubscriptionWhenAnIdempotencyKeyIsReplayed()
    {
        GivenExistingCustomer();
        var original = new SubscriptionBuilder().WithId(88).Build();
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(original);

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(UserName, "eshop-pro", "key-1"));

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.Equal(88, result.Subscription.Id);
        await _gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DerivesTheSameSubscriptionReferenceForTheSameIdempotencyKey()
    {
        GivenExistingCustomer();
        _gateway.ListCustomerSubscriptionsAsync(CustomerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().Build());

        await CreateService().SubscribeAsync(new SubscribeRequest(UserName, "eshop-pro", "key-1"));
        await CreateService().SubscribeAsync(new SubscribeRequest(UserName, "eshop-pro", "key-1"));

        var references = _gateway.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IBillingGateway.CreateSubscriptionAsync))
            .Select(c => ((NewSubscription)c.GetArguments()[0]!).Reference)
            .ToList();

        Assert.Equal(2, references.Count);
        Assert.Equal(references[0], references[1]);
    }

    [Fact]
    public async Task ReturnsTheWinningSubscriptionWhenTheProviderRejectsADuplicateReference()
    {
        GivenExistingCustomer();
        _gateway.ListCustomerSubscriptionsAsync(CustomerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Throws(new BillingRequestRejectedException("rejected", new[] { "Reference: must be unique - that value has been taken." }));

        var winner = new SubscriptionBuilder().WithId(99).Build();
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CustomerSubscription?)null, winner);

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(UserName, "eshop-pro", "key-1"));

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.Equal(99, result.Subscription.Id);
    }

    [Fact]
    public async Task SurfacesARejectionThatIsNotAboutADuplicateReference()
    {
        GivenExistingCustomer();
        _gateway.ListCustomerSubscriptionsAsync(CustomerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Throws(new BillingRequestRejectedException("rejected", new[] { "No payment method was on file for the $299.00 balance" }));
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CustomerSubscription?)null);

        await Assert.ThrowsAsync<BillingRequestRejectedException>(
            () => CreateService().SubscribeAsync(new SubscribeRequest(UserName, "eshop-pro")));
    }

    [Fact]
    public async Task RecoversWhenAConcurrentRequestCreatedTheCustomerFirst()
    {
        _gateway.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null, new BillingCustomer(CustomerId, CustomerReference, "Shopper", "Example", UserName));
        _gateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>())
            .Throws(new BillingRequestRejectedException("rejected", new[] { "Reference: must be unique - that value has been taken." }));
        _gateway.ListCustomerSubscriptionsAsync(CustomerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().Build());

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(UserName, "eshop-pro"));

        Assert.Equal(CustomerId, result.Customer.Id);
        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
    }

    [Fact]
    public async Task CreatesOnlyOneSubscriptionWhenTheSameShopperSubscribesConcurrently()
    {
        GivenExistingCustomer();

        var created = 0;
        var subscriptions = new List<CustomerSubscription>();

        _gateway.ListCustomerSubscriptionsAsync(CustomerId, Arg.Any<CancellationToken>())
            .Returns(_ => subscriptions.ToArray());

        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref created);
                var subscription = new SubscriptionBuilder().WithId(1).Build();
                subscriptions.Add(subscription);
                return subscription;
            });

        var service = CreateService();
        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => service.SubscribeAsync(new SubscribeRequest(UserName, "eshop-pro"))));

        Assert.Equal(1, created);
        Assert.Equal(1, results.Count(r => r.Outcome == SubscribeOutcome.Created));
        Assert.All(results, r => Assert.Equal(1, r.Subscription.Id));
    }

    [Fact]
    public async Task RejectsAPlanHandleThatIsNotInTheConfiguredProductFamily()
    {
        GivenExistingCustomer();

        var exception = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(new SubscribeRequest(UserName, "not-a-plan")));

        Assert.Equal("not-a-plan", exception.PlanHandle);
        Assert.Contains("eshop-pro", exception.Message);
    }

    [Fact]
    public async Task RejectsASubscribeWithNoPlanHandleAndNoConfiguredDefault()
    {
        await Assert.ThrowsAsync<BillingRequestRejectedException>(
            () => CreateService().SubscribeAsync(new SubscribeRequest(UserName)));
    }

    [Fact]
    public async Task FallsBackToTheConfiguredDefaultPlanHandle()
    {
        _options.DefaultPlanHandle = "eshop-pro";
        GivenExistingCustomer();
        _gateway.ListCustomerSubscriptionsAsync(CustomerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().Build());

        await CreateService().SubscribeAsync(new SubscribeRequest(UserName));

        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewSubscription>(s => s.PlanHandle == "eshop-pro"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UsesAnInvoicingCollectionMethodWhenThePlanNeedsNoPaymentProfile()
    {
        GivenExistingCustomer();
        _gateway.ListCustomerSubscriptionsAsync(CustomerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().Build());

        await CreateService().SubscribeAsync(new SubscribeRequest(UserName, "eshop-pro"));

        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewSubscription>(s => s.PaymentCollectionMethod == "remittance"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeavesTheSiteDefaultCollectionMethodAloneWhenThePlanRequiresAPaymentProfile()
    {
        _gateway.ListPlansAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan> { new SubscriptionPlanBuilder().RequiringPaymentMethod().Build() });
        GivenExistingCustomer();
        _gateway.ListCustomerSubscriptionsAsync(CustomerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().Build());

        await CreateService().SubscribeAsync(new SubscribeRequest(UserName, "eshop-pro"));

        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewSubscription>(s => s.PaymentCollectionMethod == null), Arg.Any<CancellationToken>());
        await _gateway.DidNotReceive().GetSiteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAUserThatTheIdentityStoreDoesNotKnow()
    {
        _directory.FindByUserNameAsync("ghost@example.com", Arg.Any<CancellationToken>())
            .Returns((SubscriberContact?)null);

        await Assert.ThrowsAsync<SubscriberNotFoundException>(
            () => CreateService().SubscribeAsync(new SubscribeRequest("ghost@example.com", "eshop-pro")));
    }

    private void GivenExistingCustomer() =>
        _gateway.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(CustomerId, CustomerReference, "Shopper", "Example", UserName));
}
