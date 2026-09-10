using System.Collections.Generic;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class SubscriptionServiceTests
{
    private const string Reference = "user-1";

    private readonly IMaxioGateway _gateway = Substitute.For<IMaxioGateway>();
    private readonly SubscriptionService _service;

    public SubscriptionServiceTests()
    {
        _service = new SubscriptionService(
            _gateway,
            Substitute.For<IAppLogger<SubscriptionService>>(),
            new KeyedAsyncLock());

        _gateway.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(Plans());
    }

    private static IReadOnlyList<SubscriptionPlan> Plans() => new List<SubscriptionPlan>
    {
        new() { Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" },
        new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
    };

    private static CustomerRegistration Registration() => new(Reference, "u@example.com", "U", "Example");

    private static MaxioCustomer Customer() => new() { Id = 42, Reference = Reference, Email = "u@example.com" };

    private static CustomerSubscription Subscription(string handle, string state, long id = 100) => new()
    {
        Id = id,
        State = state,
        ProductHandle = handle,
        ProductName = handle,
    };

    [Fact]
    public async Task SubscribeCreatesNewSubscriptionWhenNoneExists()
    {
        _gateway.FindCustomerByReferenceAsync(Reference, Arg.Any<CancellationToken>()).Returns(Customer());
        _gateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<CustomerSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription("eshop-pro", "active", 101));

        var result = await _service.SubscribeAsync(new SubscribeCommand(Registration(), "eshop-pro"));

        Assert.False(result.AlreadyExisted);
        Assert.Equal(101, result.Subscription.Id);
        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewSubscription>(n => n.CustomerId == 42 && n.ProductHandle == "eshop-pro"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeReturnsExistingLiveSubscriptionWithoutCreating()
    {
        _gateway.FindCustomerByReferenceAsync(Reference, Arg.Any<CancellationToken>()).Returns(Customer());
        _gateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<CustomerSubscription> { Subscription("eshop-pro", "active", 200) });

        var result = await _service.SubscribeAsync(new SubscribeCommand(Registration(), "eshop-pro"));

        Assert.True(result.AlreadyExisted);
        Assert.Equal(200, result.Subscription.Id);
        await _gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeIgnoresCanceledSubscriptionAndCreatesNew()
    {
        _gateway.FindCustomerByReferenceAsync(Reference, Arg.Any<CancellationToken>()).Returns(Customer());
        _gateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<CustomerSubscription> { Subscription("eshop-pro", "canceled", 300) });
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription("eshop-pro", "active", 301));

        var result = await _service.SubscribeAsync(new SubscribeCommand(Registration(), "eshop-pro"));

        Assert.False(result.AlreadyExisted);
        Assert.Equal(301, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeCreatesCustomerWhenNoneExists()
    {
        _gateway.FindCustomerByReferenceAsync(Reference, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _gateway.CreateCustomerAsync(Arg.Any<CustomerRegistration>(), Arg.Any<CancellationToken>()).Returns(Customer());
        _gateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<CustomerSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription("eshop-pro", "active", 400));

        await _service.SubscribeAsync(new SubscribeCommand(Registration(), "eshop-pro"));

        await _gateway.Received(1).CreateCustomerAsync(
            Arg.Is<CustomerRegistration>(r => r.Reference == Reference), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeRecoversWhenCustomerReferenceAlreadyTaken()
    {
        // First lookup misses; create loses a race (reference taken); re-lookup finds the customer.
        _gateway.FindCustomerByReferenceAsync(Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, Customer());
        _gateway.CreateCustomerAsync(Arg.Any<CustomerRegistration>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(422, new[] { "reference: has already been taken" }));
        _gateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<CustomerSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription("eshop-pro", "active", 500));

        var result = await _service.SubscribeAsync(new SubscribeCommand(Registration(), "eshop-pro"));

        Assert.Equal(500, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeThrowsForUnknownPlan()
    {
        await Assert.ThrowsAsync<PlanNotFoundException>(() =>
            _service.SubscribeAsync(new SubscribeCommand(Registration(), "no-such-plan")));
    }

    [Fact]
    public async Task SubscribeDefaultsToFirstPlanWhenHandleOmitted()
    {
        _gateway.FindCustomerByReferenceAsync(Reference, Arg.Any<CancellationToken>()).Returns(Customer());
        _gateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<CustomerSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription("basic-plan", "active", 600));

        await _service.SubscribeAsync(new SubscribeCommand(Registration(), null));

        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewSubscription>(n => n.ProductHandle == "basic-plan"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubscriptionsReturnsEmptyWhenNoCustomer()
    {
        _gateway.FindCustomerByReferenceAsync(Reference, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var result = await _service.GetSubscriptionsAsync(Reference);

        Assert.Empty(result);
        await _gateway.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }
}
