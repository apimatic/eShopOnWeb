using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string Reference = "shopper@example.com";
    private const string PlanHandle = "eshop-pro";

    private readonly IMaxioClient _client = Substitute.For<IMaxioClient>();
    private readonly MaxioSettings _settings = new()
    {
        ApiKey = "key",
        Subdomain = "test-site",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private MaxioSubscriptionService CreateService() =>
        new(_client, _settings, NullLogger<MaxioSubscriptionService>.Instance);

    private void OfferProPlan()
    {
        _client.ListPlansAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan>
            {
                new() { Handle = PlanHandle, Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
            });
    }

    private SubscribeRequest NewRequest() => new(Reference, Reference, PlanHandle);

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        OfferProPlan();
        _client.FindCustomerByReferenceAsync(Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomerReference?)null);
        _client.CreateCustomerAsync(Reference, Reference, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerReference(42, Reference, Reference));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<CustomerSubscription>());
        _client.CreateSubscriptionAsync(42, PlanHandle, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CustomerSubscription { Id = 100, State = "active", PlanHandle = PlanHandle, CustomerId = 42 });

        var result = await CreateService().SubscribeAsync(NewRequest());

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(100, result.Subscription.Id);
        await _client.Received(1).CreateCustomerAsync(Reference, Reference, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(42, PlanHandle, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ReusesExistingCustomer_WhenAlreadyPresent()
    {
        OfferProPlan();
        _client.FindCustomerByReferenceAsync(Reference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerReference(7, Reference, Reference));
        _client.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new List<CustomerSubscription>());
        _client.CreateSubscriptionAsync(7, PlanHandle, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CustomerSubscription { Id = 200, State = "active", PlanHandle = PlanHandle, CustomerId = 7 });

        var result = await CreateService().SubscribeAsync(NewRequest());

        Assert.False(result.AlreadySubscribed);
        await _client.DidNotReceive().CreateCustomerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_IsIdempotent_WhenLiveSubscriptionAlreadyExists()
    {
        OfferProPlan();
        _client.FindCustomerByReferenceAsync(Reference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerReference(7, Reference, Reference));
        _client.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new List<CustomerSubscription>
            {
                new() { Id = 300, State = "active", PlanHandle = PlanHandle, CustomerId = 7 }
            });

        var result = await CreateService().SubscribeAsync(NewRequest());

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(300, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_DoesNotTreatCanceledSubscriptionAsLive()
    {
        OfferProPlan();
        _client.FindCustomerByReferenceAsync(Reference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerReference(7, Reference, Reference));
        _client.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new List<CustomerSubscription>
            {
                new() { Id = 400, State = "canceled", PlanHandle = PlanHandle, CustomerId = 7 }
            });
        _client.CreateSubscriptionAsync(7, PlanHandle, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CustomerSubscription { Id = 401, State = "active", PlanHandle = PlanHandle, CustomerId = 7 });

        var result = await CreateService().SubscribeAsync(NewRequest());

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(401, result.Subscription.Id);
        await _client.Received(1).CreateSubscriptionAsync(7, PlanHandle, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_Throws_WhenPlanNotOffered()
    {
        OfferProPlan();
        var request = new SubscribeRequest(Reference, Reference, "not-a-plan");

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() => CreateService().SubscribeAsync(request));
    }

    [Fact]
    public async Task GetSubscriptionsForUser_ReturnsEmpty_WhenNoCustomer()
    {
        _client.FindCustomerByReferenceAsync(Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomerReference?)null);

        var subscriptions = await CreateService().GetSubscriptionsForUserAsync(Reference);

        Assert.Empty(subscriptions);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }
}
