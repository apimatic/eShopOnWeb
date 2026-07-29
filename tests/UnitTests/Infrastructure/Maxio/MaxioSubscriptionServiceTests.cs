using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string ProPlan = "eshop-pro";

    private readonly IMaxioClient _client = Substitute.For<IMaxioClient>();
    private readonly SubscriberIdentity _subscriber = new("user-123", "shopper@example.com", null, null);

    private MaxioSubscriptionService CreateService(string? defaultPlanHandle = null)
    {
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "acme",
            ProductFamilyHandle = FamilyHandle,
            DefaultPlanHandle = defaultPlanHandle,
        });

        return new MaxioSubscriptionService(_client, settings, NullLogger<MaxioSubscriptionService>.Instance);
    }

    private void SeedPlans()
    {
        _client.ListProductsForProductFamilyAsync($"handle:{FamilyHandle}", Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Id = 1, Handle = ProPlan, Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
                new() { Id = 2, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" },
            });
    }

    [Fact]
    public async Task GetPlans_MapsProductsToPlans()
    {
        SeedPlans();
        var service = CreateService();

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = Assert.Single(plans, p => p.Handle == ProPlan);
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("$299.00", pro.FormattedPrice);
        Assert.Equal("month", pro.IntervalUnit);
    }

    [Fact]
    public async Task Subscribe_WhenNoCustomer_CreatesCustomerThenSubscription()
    {
        SeedPlans();
        _client.LookupCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 500, Reference = _subscriber.UserId });
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription
            {
                Id = 9001, State = "active",
                Product = new MaxioProduct { Handle = ProPlan, Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
                Customer = new MaxioCustomer { Id = 500 },
            });

        var service = CreateService();
        var result = await service.SubscribeAsync(_subscriber, ProPlan);

        Assert.False(result.AlreadyExisted);
        Assert.Equal(9001, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomer>(c => c.Reference == _subscriber.UserId && c.Email == _subscriber.Email),
            Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(s => s.ProductHandle == ProPlan && s.CustomerId == 500 && s.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_WhenCustomerExists_DoesNotCreateCustomer()
    {
        SeedPlans();
        _client.LookupCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 500, Reference = _subscriber.UserId });
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 9002, State = "active", Product = new MaxioProduct { Handle = ProPlan } });

        var service = CreateService();
        await service.SubscribeAsync(_subscriber, ProPlan);

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_WhenLiveSubscriptionExists_ReusesItAndDoesNotCreate()
    {
        SeedPlans();
        _client.LookupCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 500 });
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new() { Id = 7777, State = "active", Product = new MaxioProduct { Handle = ProPlan, Name = "Pro Plan", PriceInCents = 29900 } },
            });

        var service = CreateService();
        var result = await service.SubscribeAsync(_subscriber, ProPlan);

        Assert.True(result.AlreadyExisted);
        Assert.Equal(7777, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_WhenCanceledSubscriptionExists_CreatesNewOne()
    {
        SeedPlans();
        _client.LookupCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 500 });
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new() { Id = 7777, State = "canceled", Product = new MaxioProduct { Handle = ProPlan } },
            });
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 8888, State = "active", Product = new MaxioProduct { Handle = ProPlan } });

        var service = CreateService();
        var result = await service.SubscribeAsync(_subscriber, ProPlan);

        Assert.False(result.AlreadyExisted);
        Assert.Equal(8888, result.Subscription.Id);
        await _client.Received(1).CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_WhenCreateCustomerLosesRace_RecoversByLookup()
    {
        SeedPlans();
        // First lookup: absent. Create fails with 422 (reference taken). Second lookup: found.
        _client.LookupCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, new MaxioCustomer { Id = 500 });
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(HttpStatusCode.UnprocessableEntity, new[] { "reference: has already been taken" }));
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 9003, State = "active", Product = new MaxioProduct { Handle = ProPlan } });

        var service = CreateService();
        var result = await service.SubscribeAsync(_subscriber, ProPlan);

        Assert.Equal(9003, result.Subscription.Id);
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(s => s.CustomerId == 500), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_WithUnknownPlan_ThrowsPlanNotFound()
    {
        SeedPlans();
        var service = CreateService();

        await Assert.ThrowsAsync<PlanNotFoundException>(() => service.SubscribeAsync(_subscriber, "ghost-plan"));
    }

    [Fact]
    public async Task Subscribe_WithNoPlanAndNoDefault_ThrowsSubscriptionException()
    {
        SeedPlans();
        var service = CreateService(defaultPlanHandle: null);

        await Assert.ThrowsAsync<SubscriptionException>(() => service.SubscribeAsync(_subscriber, null));
    }

    [Fact]
    public async Task Subscribe_WithNoPlan_UsesConfiguredDefault()
    {
        SeedPlans();
        _client.LookupCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 500 });
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 9004, State = "active", Product = new MaxioProduct { Handle = ProPlan } });

        var service = CreateService(defaultPlanHandle: ProPlan);
        await service.SubscribeAsync(_subscriber, null);

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(s => s.ProductHandle == ProPlan), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubscriptions_WhenNoCustomer_ReturnsEmpty()
    {
        _client.LookupCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var service = CreateService();
        var subs = await service.GetSubscriptionsAsync(_subscriber);

        Assert.Empty(subs);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
