using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.MaxioSubscriptionServiceTests;

public class SubscribeAsync
{
    private const string ProductFamilyHandle = "eshop-subscribe";
    private readonly MaxioOptions _options = new() { ProductFamilyHandle = ProductFamilyHandle };
    private readonly IMaxioClient _client = Substitute.For<IMaxioClient>();

    private static MaxioProduct Plan(int id, string handle) =>
        new(id, handle, "Some Plan", "description", 29900, 1, "month", null);

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenBuyerHasNeitherYet()
    {
        var plan = Plan(1, "eshop-pro");
        _client.ListProductsForFamilyAsync(ProductFamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { plan });
        _client.FindCustomerByReferenceAsync("buyer@example.com", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        var createdCustomer = new MaxioCustomer(42, "buyer@example.com", "buyer@example.com");
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(createdCustomer);
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        var createdSubscription = new MaxioSubscription(99, "active", 42, 1, "eshop-pro", "Pro Plan", 29900, null, null, DateTimeOffset.UtcNow);
        _client.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(createdSubscription);

        var service = new MaxioSubscriptionService(_client, _options);
        var (subscription, created) = await service.SubscribeAsync("buyer@example.com", "buyer@example.com", "eshop-pro");

        Assert.True(created);
        Assert.Equal(99, subscription.Id);
        await _client.Received(1).CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingSubscriptionWithoutCreatingANewOneWhenAlreadyEnrolled()
    {
        var plan = Plan(1, "eshop-pro");
        _client.ListProductsForFamilyAsync(ProductFamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { plan });
        var existingCustomer = new MaxioCustomer(42, "buyer@example.com", "buyer@example.com");
        _client.FindCustomerByReferenceAsync("buyer@example.com", Arg.Any<CancellationToken>())
            .Returns(existingCustomer);
        var existingSubscription = new MaxioSubscription(99, "active", 42, 1, "eshop-pro", "Pro Plan", 29900, null, null, DateTimeOffset.UtcNow);
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { existingSubscription });

        var service = new MaxioSubscriptionService(_client, _options);
        var (subscription, created) = await service.SubscribeAsync("buyer@example.com", "buyer@example.com", "eshop-pro");

        Assert.False(created);
        Assert.Equal(99, subscription.Id);
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    [InlineData("failed_to_create")]
    [InlineData("trial_ended")]
    public async Task CreatesANewSubscriptionWhenThePriorOneIsEndOfLife(string endOfLifeState)
    {
        var plan = Plan(1, "eshop-pro");
        _client.ListProductsForFamilyAsync(ProductFamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { plan });
        var existingCustomer = new MaxioCustomer(42, "buyer@example.com", "buyer@example.com");
        _client.FindCustomerByReferenceAsync("buyer@example.com", Arg.Any<CancellationToken>())
            .Returns(existingCustomer);
        var priorSubscription = new MaxioSubscription(90, endOfLifeState, 42, 1, "eshop-pro", "Pro Plan", 29900, null, null, DateTimeOffset.UtcNow);
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { priorSubscription });
        var newSubscription = new MaxioSubscription(99, "active", 42, 1, "eshop-pro", "Pro Plan", 29900, null, null, DateTimeOffset.UtcNow);
        _client.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(newSubscription);

        var service = new MaxioSubscriptionService(_client, _options);
        var (subscription, created) = await service.SubscribeAsync("buyer@example.com", "buyer@example.com", "eshop-pro");

        Assert.True(created);
        Assert.Equal(99, subscription.Id);
    }

    [Fact]
    public async Task ThrowsPlanNotFoundExceptionForAnUnknownPlanHandle()
    {
        _client.ListProductsForFamilyAsync(ProductFamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { Plan(1, "eshop-pro") });

        var service = new MaxioSubscriptionService(_client, _options);

        await Assert.ThrowsAsync<PlanNotFoundException>(
            () => service.SubscribeAsync("buyer@example.com", "buyer@example.com", "does-not-exist"));

        await _client.DidNotReceive().FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FallsBackToTheWinningCustomerWhenCreateCustomerConflicts()
    {
        _client.ListProductsForFamilyAsync(ProductFamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { Plan(1, "eshop-pro") });

        var winner = new MaxioCustomer(42, "buyer@example.com", "buyer@example.com");
        _client.FindCustomerByReferenceAsync("buyer@example.com", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, winner);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns<MaxioCustomer>(_ => throw new MaxioApiException(HttpStatusCode.UnprocessableEntity, "Reference has already been taken"));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        var created = new MaxioSubscription(99, "active", 42, 1, "eshop-pro", "Pro Plan", 29900, null, null, DateTimeOffset.UtcNow);
        _client.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<CancellationToken>()).Returns(created);

        var service = new MaxioSubscriptionService(_client, _options);
        var (subscription, wasCreated) = await service.SubscribeAsync("buyer@example.com", "buyer@example.com", "eshop-pro");

        Assert.True(wasCreated);
        Assert.Equal(99, subscription.Id);
    }

    [Fact]
    public async Task ConcurrentSubscribeCallsForTheSameBuyerCreateOnlyOneSubscription()
    {
        // Reproduces the double-click / two-tabs scenario: two callers race SubscribeAsync for
        // the same never-before-seen buyer and plan at the same time.
        var plan = Plan(1, "eshop-pro");
        _client.ListProductsForFamilyAsync(ProductFamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { plan });

        var buyerReference = $"racer-{Guid.NewGuid()}@example.com";
        var customer = new MaxioCustomer(7, buyerReference, buyerReference);
        MaxioCustomer? customerSoFar = null;
        _client.FindCustomerByReferenceAsync(buyerReference, Arg.Any<CancellationToken>())
            .Returns(_ => customerSoFar);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(async _ => { await Task.Delay(25); customerSoFar = customer; return customer; });

        var subscriptionsSoFar = new List<MaxioSubscription>();
        var gate = new SemaphoreSlim(1, 1);
        _client.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await gate.WaitAsync();
                try
                {
                    return (IReadOnlyList<MaxioSubscription>)new List<MaxioSubscription>(subscriptionsSoFar);
                }
                finally
                {
                    gate.Release();
                }
            });

        var created = new MaxioSubscription(123, "active", 7, 1, "eshop-pro", "Pro Plan", 29900, null, null, DateTimeOffset.UtcNow);
        _client.CreateSubscriptionAsync(7, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await gate.WaitAsync();
                try
                {
                    subscriptionsSoFar.Add(created);
                }
                finally
                {
                    gate.Release();
                }

                return created;
            });

        var service = new MaxioSubscriptionService(_client, _options);

        var results = await Task.WhenAll(
            service.SubscribeAsync(buyerReference, buyerReference, "eshop-pro"),
            service.SubscribeAsync(buyerReference, buyerReference, "eshop-pro"),
            service.SubscribeAsync(buyerReference, buyerReference, "eshop-pro"),
            service.SubscribeAsync(buyerReference, buyerReference, "eshop-pro"));

        Assert.All(results, r => Assert.Equal(123, r.Subscription.Id));
        Assert.Single(results, r => r.Created);
        await _client.Received(1).CreateSubscriptionAsync(7, "eshop-pro", Arg.Any<CancellationToken>());
        await _client.Received(1).CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
    }
}
