using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class SubscribeAsync
{
    private const string UserName = "demouser@microsoft.com";
    private const string ProPlan = "eshop-pro";

    private readonly FakeBillingGateway _gateway = new();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    public SubscribeAsync()
    {
        _gateway.Plans.Add(new SubscriptionPlan
        {
            Handle = ProPlan,
            Name = "Pro Plan",
            PriceInCents = 29900,
            Interval = 1,
            IntervalUnit = "month"
        });
    }

    private SubscriptionService CreateService() => new(_gateway, new KeyedAsyncLock(), _logger);

    private static SubscribeCommand Command(string? idempotencyKey = null) => new()
    {
        UserName = UserName,
        PlanHandle = ProPlan,
        IdempotencyKey = idempotencyKey
    };

    [Fact]
    public async Task CreatesTheBillingCustomerAndTheSubscription()
    {
        var result = await CreateService().SubscribeAsync(Command());

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(ProPlan, result.Subscription.PlanHandle);
        Assert.Equal(SubscriptionStates.Active, result.Subscription.State);
        Assert.Equal(1, _gateway.CreateCustomerCalls);
        Assert.Equal(1, _gateway.SubscriptionCount);

        var customer = await _gateway.FindCustomerByReferenceAsync(BillingCustomerReference.ForUser(UserName));
        Assert.NotNull(customer);
        Assert.Equal(UserName, customer!.Email);
    }

    [Fact]
    public async Task ReusesTheExistingBillingCustomer()
    {
        var service = CreateService();
        await service.SubscribeAsync(Command());
        await service.SubscribeAsync(new SubscribeCommand { UserName = UserName, PlanHandle = ProPlan });

        Assert.Equal(1, _gateway.CreateCustomerCalls);
    }

    [Fact]
    public async Task ReturnsTheExistingSubscriptionInsteadOfEnrollingTwice()
    {
        var service = CreateService();
        var first = await service.SubscribeAsync(Command());
        var second = await service.SubscribeAsync(Command());

        Assert.True(second.AlreadySubscribed);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Equal(1, _gateway.SubscriptionCount);
        Assert.Equal(1, _gateway.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task ConcurrentSubscribesProduceASingleSubscription()
    {
        var service = CreateService();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(_ => service.SubscribeAsync(Command())));

        Assert.Equal(1, _gateway.SubscriptionCount);
        Assert.Equal(1, results.Count(r => !r.AlreadySubscribed));
        Assert.Single(results.Select(r => r.Subscription.Id).Distinct());
    }

    [Fact]
    public async Task RecoversWhenAnotherRequestCreatedTheCustomerFirst()
    {
        // Stage the race: the customer appears between our lookup and our create.
        _gateway.BeforeCreateCustomer = () =>
        {
            _gateway.BeforeCreateCustomer = null;
            _gateway.SeedCustomer(new BillingCustomer
            {
                Id = 4242,
                Reference = BillingCustomerReference.ForUser(UserName),
                Email = UserName
            });
            return Task.CompletedTask;
        };

        var result = await CreateService().SubscribeAsync(Command());

        Assert.Equal(4242, result.Subscription.CustomerId);
        Assert.Equal(1, _gateway.SubscriptionCount);
    }

    [Fact]
    public async Task RecoversWhenAnotherRequestUsedTheSameIdempotencyKeyFirst()
    {
        var command = Command(idempotencyKey: "checkout-1");

        // Race in a subscription carrying the reference this call is about to use.
        _gateway.BeforeCreateSubscription = async () =>
        {
            _gateway.BeforeCreateSubscription = null;
            var winner = await new SubscriptionService(_gateway, new KeyedAsyncLock(), _logger)
                .SubscribeAsync(command);
            Assert.False(winner.AlreadySubscribed);
        };

        var result = await CreateService().SubscribeAsync(command);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(1, _gateway.SubscriptionCount);
    }

    [Fact]
    public async Task LetsTheShopperResubscribeAfterCancellation()
    {
        var customerReference = BillingCustomerReference.ForUser(UserName);
        _gateway.SeedCustomer(new BillingCustomer { Id = 77, Reference = customerReference, Email = UserName });
        _gateway.SeedSubscription(new CustomerSubscription
        {
            Id = 1,
            CustomerId = 77,
            PlanHandle = ProPlan,
            State = SubscriptionStates.Canceled,
            CanceledAt = DateTimeOffset.UtcNow.AddDays(-1)
        });

        var result = await CreateService().SubscribeAsync(Command());

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(2, _gateway.SubscriptionCount);
    }

    [Theory]
    [InlineData(SubscriptionStates.PastDue)]
    [InlineData(SubscriptionStates.Trialing)]
    [InlineData(SubscriptionStates.OnHold)]
    public async Task TreatsANonTerminalSubscriptionAsStillHeld(string state)
    {
        var customerReference = BillingCustomerReference.ForUser(UserName);
        _gateway.SeedCustomer(new BillingCustomer { Id = 77, Reference = customerReference, Email = UserName });
        _gateway.SeedSubscription(new CustomerSubscription
        {
            Id = 1,
            CustomerId = 77,
            PlanHandle = ProPlan,
            State = state
        });

        var result = await CreateService().SubscribeAsync(Command());

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(1, _gateway.SubscriptionCount);
    }

    [Fact]
    public async Task RejectsAnUnknownPlan()
    {
        var command = new SubscribeCommand { UserName = UserName, PlanHandle = "not-on-offer" };

        var exception = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(command));

        Assert.Equal("not-on-offer", exception.PlanHandle);
        Assert.Equal(0, _gateway.CreateCustomerCalls);
    }

    [Fact]
    public async Task RefusesAPlanThatNeedsAStoredPaymentMethod()
    {
        _gateway.Plans.Clear();
        _gateway.Plans.Add(new SubscriptionPlan
        {
            Handle = ProPlan,
            Name = "Pro Plan",
            PriceInCents = 29900,
            RequiresPaymentMethod = true
        });

        await Assert.ThrowsAsync<PaymentMethodRequiredException>(() => CreateService().SubscribeAsync(Command()));

        Assert.Equal(0, _gateway.SubscriptionCount);
    }

    [Fact]
    public async Task RethrowsADuplicateReferenceFailureItCannotResolve()
    {
        var gateway = Substitute.For<IBillingGateway>();
        gateway.FindPlanAsync(ProPlan, default).Returns(_gateway.Plans[0]);
        gateway.FindCustomerByReferenceAsync(Arg.Any<string>(), default)
            .Returns(new BillingCustomer { Id = 5, Reference = BillingCustomerReference.ForUser(UserName) });
        gateway.ListCustomerSubscriptionsAsync(5, default)
            .Returns(Array.Empty<CustomerSubscription>());
        gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), default)
            .Returns<Task<CustomerSubscription>>(_ => throw new BillingGatewayException(
                "duplicate", 422, new[] { "Reference: must be unique." }, isDuplicateReference: true));
        gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), default)
            .Returns((CustomerSubscription?)null);

        var service = new SubscriptionService(gateway, new KeyedAsyncLock(), _logger);

        await Assert.ThrowsAsync<BillingGatewayException>(
            () => service.SubscribeAsync(Command(idempotencyKey: "checkout-2")));
    }
}
