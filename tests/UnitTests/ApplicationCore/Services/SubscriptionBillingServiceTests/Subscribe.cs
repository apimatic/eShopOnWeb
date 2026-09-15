using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class Subscribe
{
    private readonly IMaxioBillingClient _maxio = Substitute.For<IMaxioBillingClient>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly SubscriberIdentity _subscriber = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        var plan = new SubscriptionPlan { Handle = "eshop-pro", Name = "Pro Plan", Price = 299m, Interval = 1, IntervalUnit = "month" };
        var customer = new BillingCustomer { Id = 42, Reference = _subscriber.UserId, Email = _subscriber.Email };
        var created = new CustomerSubscription
        {
            Id = 99,
            ProductHandle = "eshop-pro",
            ProductName = "Pro Plan",
            Price = 299m,
            State = "active",
            NextBillingDate = DateTimeOffset.UtcNow.AddMonths(1)
        };

        _maxio.FindCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        _maxio.CreateCustomerAsync(_subscriber.UserId, Arg.Any<string>(), Arg.Any<string>(), _subscriber.Email, Arg.Any<CancellationToken>())
            .Returns(customer);
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((CustomerSubscription?)null);
        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new List<SubscriptionPlan> { plan });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<CustomerSubscription>());
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(created);

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(_subscriber, "eshop-pro");

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        await _maxio.Received(1).CreateCustomerAsync(_subscriber.UserId, Arg.Any<string>(), Arg.Any<string>(), _subscriber.Email, Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(42, "eshop-pro", "user-1:eshop-pro", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingSubscriptionOnRepeatSubscribe()
    {
        var existing = new CustomerSubscription
        {
            Id = 7,
            ProductHandle = "eshop-pro",
            ProductName = "Pro Plan",
            Price = 299m,
            State = "active"
        };

        _maxio.FindCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = _subscriber.UserId, Email = _subscriber.Email });
        _maxio.FindSubscriptionByReferenceAsync("user-1:eshop-pro", Arg.Any<CancellationToken>()).Returns(existing);

        var service = new SubscriptionBillingService(_maxio, _logger);
        var first = await service.SubscribeAsync(_subscriber, "eshop-pro");
        var second = await service.SubscribeAsync(_subscriber, "eshop-pro");

        Assert.Equal(7, first.Id);
        Assert.Equal(7, second.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThrowsWhenPlanIsNotInConfiguredFamily()
    {
        _maxio.FindCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = _subscriber.UserId, Email = _subscriber.Email });
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((CustomerSubscription?)null);
        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new List<SubscriptionPlan>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", Interval = 1, IntervalUnit = "month" }
        });

        var service = new SubscriptionBillingService(_maxio, _logger);

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(_subscriber, "unknown-plan"));
    }

    [Fact]
    public async Task RecoversWhenCreateCustomerLosesTheRace()
    {
        var customer = new BillingCustomer { Id = 42, Reference = _subscriber.UserId, Email = _subscriber.Email };
        var plan = new SubscriptionPlan { Handle = "basic-plan", Name = "Basic", Price = 29m, Interval = 1, IntervalUnit = "month" };
        var created = new CustomerSubscription { Id = 11, ProductHandle = "basic-plan", ProductName = "Basic", Price = 29m, State = "active" };

        _maxio.FindCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null, customer);
        _maxio.When(x => x.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Throw(new MaxioApiException(HttpStatusCode.UnprocessableEntity, "reference taken"));
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((CustomerSubscription?)null);
        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new List<SubscriptionPlan> { plan });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<CustomerSubscription>());
        _maxio.CreateSubscriptionAsync(42, "basic-plan", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(created);

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(_subscriber, "basic-plan");

        Assert.Equal(11, result.Id);
    }

    [Fact]
    public async Task GetSubscriptionsReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.FindCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.GetSubscriptionsAsync(_subscriber);

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
