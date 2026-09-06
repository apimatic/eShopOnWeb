using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string ProPlan = "eshop-pro";
    private const string BasicPlan = "basic-plan";

    private readonly FakeMaxioApiClient _client = new();
    private readonly MaxioSubscriptionService _service;
    private readonly SubscriberIdentity _subscriber = new("demouser@microsoft.com", "demouser@microsoft.com");

    public MaxioSubscriptionServiceTests()
    {
        _client.AddProduct(ProPlan, "Pro Plan", 29900);
        _client.AddProduct(BasicPlan, "Basic Plan", 2900);

        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        });

        _service = new MaxioSubscriptionService(_client, options, NullLogger<MaxioSubscriptionService>.Instance);
    }

    [Fact]
    public async Task ListsPlansCheapestFirst()
    {
        var plans = await _service.ListPlansAsync();

        Assert.Equal(new[] { BasicPlan, ProPlan }, plans.Select(p => p.Handle));
        Assert.Equal(29m, plans.First().Price);
    }

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscription()
    {
        var result = await _service.SubscribeAsync(new SubscribeRequest(_subscriber, ProPlan));

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.True(result.Created);
        Assert.Equal(ProPlan, result.Subscription.PlanHandle);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.True(result.Subscription.IsLive);
        Assert.NotNull(result.Subscription.NextBillingAt);
        Assert.Equal("eshoponweb:demouser@microsoft.com:eshop-pro", result.Subscription.Reference);
        Assert.Equal(1, _client.CreateCustomerCalls);
        Assert.Equal(1, _client.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task SubscribeTwiceReturnsTheSameSubscription()
    {
        var first = await _service.SubscribeAsync(new SubscribeRequest(_subscriber, ProPlan));
        var second = await _service.SubscribeAsync(new SubscribeRequest(_subscriber, ProPlan));

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, second.Outcome);
        Assert.False(second.Created);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Equal(1, _client.CreateCustomerCalls);
        Assert.Equal(1, _client.CreateSubscriptionCalls);
        Assert.Single(_client.Subscriptions);
    }

    [Fact]
    public async Task ConcurrentSubscribesCreateOnlyOneSubscription()
    {
        var attempts = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(() => _service.SubscribeAsync(new SubscribeRequest(_subscriber, ProPlan))))
            .ToArray();

        var results = await Task.WhenAll(attempts);

        Assert.Single(_client.Subscriptions);
        Assert.Single(results, r => r.Created);
        Assert.Equal(1, _client.CreateCustomerCalls);
        Assert.All(results, r => Assert.Equal(results[0].Subscription.Id, r.Subscription.Id));
    }

    [Fact]
    public async Task ASubscriptionCreatedElsewhereUnderTheSameReferenceIsAdopted()
    {
        // The in-process guard cannot see other instances of the app, so the real protection is the
        // reference Maxio enforces uniqueness on. Simulate losing that race: another writer takes the
        // reference between our "already subscribed?" read and our create.
        const string customerReference = "eshoponweb:demouser@microsoft.com";

        var customer = await _client.CreateCustomerAsync(new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer { Reference = customerReference, Email = _subscriber.Email }
        });

        _client.BeforeWrite = async () =>
        {
            _client.BeforeWrite = null; // interfere exactly once
            await _client.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioCreateSubscription
                {
                    ProductHandle = ProPlan,
                    CustomerId = customer.Id,
                    Reference = $"{customerReference}:{ProPlan}"
                }
            });
        };

        var result = await _service.SubscribeAsync(new SubscribeRequest(_subscriber, ProPlan));

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.False(result.Created);
        Assert.Single(_client.Subscriptions);
    }

    [Fact]
    public async Task IdempotencyKeyReplayReturnsTheOriginalSubscription()
    {
        var first = await _service.SubscribeAsync(new SubscribeRequest(_subscriber, ProPlan, "checkout-42"));
        var replay = await _service.SubscribeAsync(new SubscribeRequest(_subscriber, ProPlan, "checkout-42"));

        Assert.Equal(SubscribeOutcome.Created, first.Outcome);
        Assert.Equal(SubscribeOutcome.IdempotentReplay, replay.Outcome);
        Assert.Equal(first.Subscription.Id, replay.Subscription.Id);
        Assert.Equal("eshoponweb:demouser@microsoft.com:key:checkout-42", replay.Subscription.Reference);
        Assert.Single(_client.Subscriptions);
    }

    [Fact]
    public async Task DifferentPlansProduceSeparateSubscriptions()
    {
        var pro = await _service.SubscribeAsync(new SubscribeRequest(_subscriber, ProPlan));
        var basic = await _service.SubscribeAsync(new SubscribeRequest(_subscriber, BasicPlan));

        Assert.Equal(SubscribeOutcome.Created, basic.Outcome);
        Assert.NotEqual(pro.Subscription.Id, basic.Subscription.Id);
        Assert.Equal(2, _client.Subscriptions.Count);
    }

    [Fact]
    public async Task ResubscribingAfterCancellationCreatesANewSubscription()
    {
        var first = await _service.SubscribeAsync(new SubscribeRequest(_subscriber, ProPlan));
        _client.Cancel(first.Subscription.Id);

        var second = await _service.SubscribeAsync(new SubscribeRequest(_subscriber, ProPlan));

        Assert.Equal(SubscribeOutcome.Created, second.Outcome);
        Assert.NotEqual(first.Subscription.Id, second.Subscription.Id);
        Assert.Equal("eshoponweb:demouser@microsoft.com:eshop-pro:2", second.Subscription.Reference);
    }

    [Fact]
    public async Task SubscribingToAnUnknownPlanIsRejected()
    {
        var exception = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => _service.SubscribeAsync(new SubscribeRequest(_subscriber, "not-on-this-site")));

        Assert.Equal("not-on-this-site", exception.PlanHandle);
        Assert.Empty(_client.Subscriptions);
    }

    [Fact]
    public async Task PlansRequiringAPaymentMethodAreRejectedBeforeAnyWrite()
    {
        _client.AddProduct("card-required", "Card Required", 1000, requireCreditCard: true);

        await Assert.ThrowsAsync<PaymentMethodRequiredException>(
            () => _service.SubscribeAsync(new SubscribeRequest(_subscriber, "card-required")));

        Assert.Equal(0, _client.CreateCustomerCalls);
        Assert.Empty(_client.Subscriptions);
    }

    [Fact]
    public async Task ListingSubscriptionsForAShopperWhoNeverSubscribedIsEmpty()
    {
        var subscriptions = await _service.ListSubscriptionsAsync(_subscriber);

        Assert.Empty(subscriptions);
        Assert.Equal(0, _client.CreateCustomerCalls);
    }

    [Fact]
    public async Task ListingSubscriptionsReturnsNewestFirst()
    {
        await _service.SubscribeAsync(new SubscribeRequest(_subscriber, ProPlan));
        await _service.SubscribeAsync(new SubscribeRequest(_subscriber, BasicPlan));

        var subscriptions = await _service.ListSubscriptionsAsync(_subscriber);

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal(BasicPlan, subscriptions.First().PlanHandle);
    }

    [Fact]
    public async Task SubscriptionsBelongOnlyToTheirOwnShopper()
    {
        var other = new SubscriberIdentity("admin@microsoft.com", "admin@microsoft.com");

        await _service.SubscribeAsync(new SubscribeRequest(_subscriber, ProPlan));

        Assert.Empty(await _service.ListSubscriptionsAsync(other));
        Assert.Single(await _service.ListSubscriptionsAsync(_subscriber));
    }
}
