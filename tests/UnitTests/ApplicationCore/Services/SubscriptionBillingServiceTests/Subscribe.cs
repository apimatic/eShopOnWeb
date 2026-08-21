using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class Subscribe
{
    private readonly ShopperBillingIdentity _shopper =
        new("user-guid-1", "demouser@microsoft.com", "demouser@microsoft.com");

    [Fact]
    public async Task Subscribe_CreatesCustomerOnceAndReturnsNewSubscription()
    {
        var maxio = new FakeMaxio();
        maxio.Products.Add(ProPlan());
        var service = CreateService(maxio);

        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Created);
        Assert.Equal("eshop-pro", result.Value.Subscription.ProductHandle);
        Assert.Equal("active", result.Value.Subscription.State);
        Assert.Equal(29900, result.Value.Subscription.PriceInCents);
        Assert.Equal(1, maxio.CreateCustomerCalls);
        Assert.Equal(1, maxio.CreateSubscriptionCalls);
        Assert.Equal(_shopper.UserId, maxio.Customers.Keys.Single());
    }

    [Fact]
    public async Task Subscribe_IsIdempotentForTheSameLivePlan()
    {
        var maxio = new FakeMaxio();
        maxio.Products.Add(ProPlan());
        var service = CreateService(maxio);

        var first = await service.SubscribeAsync(_shopper, "eshop-pro");
        var second = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(first.Value.Created);
        Assert.False(second.Value.Created);
        Assert.Equal(first.Value.Subscription.Id, second.Value.Subscription.Id);
        Assert.Equal(1, maxio.CreateCustomerCalls);
        Assert.Equal(1, maxio.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task Subscribe_DoesNotCreateASecondCustomerWhenLookupFindsTheExistingReference()
    {
        var maxio = new FakeMaxio();
        maxio.Products.Add(ProPlan());
        maxio.Customers[_shopper.UserId] = new MaxioCustomerInfo(42, _shopper.UserId);
        var service = CreateService(maxio);

        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.IsSuccess);
        Assert.Equal(0, maxio.CreateCustomerCalls);
        Assert.Equal(1, maxio.CreateSubscriptionCalls);
        Assert.Equal(42, maxio.LastCreatedCustomerId);
    }

    [Fact]
    public async Task Subscribe_ReturnsNotFoundForUnknownPlanHandle()
    {
        var maxio = new FakeMaxio();
        maxio.Products.Add(ProPlan());
        var service = CreateService(maxio);

        var result = await service.SubscribeAsync(_shopper, "not-a-plan");

        Assert.Equal(Ardalis.Result.ResultStatus.NotFound, result.Status);
        Assert.Equal(0, maxio.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task ListPlans_ReturnsConfiguredFamilyProducts()
    {
        var maxio = new FakeMaxio();
        maxio.Products.Add(ProPlan());
        maxio.Products.Add(new MaxioProductInfo("basic-plan", "Basic Plan", null, 2900, 1, "month", "eshop-subscribe"));
        var service = CreateService(maxio);

        var result = await service.ListPlansAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("basic-plan", result.Value[0].Handle);
        Assert.Equal(29.00m, result.Value[0].Price);
        Assert.Equal("eshop-pro", result.Value[1].Handle);
    }

    [Fact]
    public async Task ListMySubscriptions_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        var service = CreateService(new FakeMaxio());

        var result = await service.ListMySubscriptionsAsync(_shopper);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void NamesFor_UsesEmailLocalPart()
    {
        var (first, last) = SubscriptionBillingService.NamesFor(_shopper);

        Assert.Equal("Demouser", first);
        Assert.Equal("eShopOnWeb", last);
    }

    private static SubscriptionBillingService CreateService(IMaxioAdvancedBillingClient maxio)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = "eshop-subscribe"
        });
        var logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
        return new SubscriptionBillingService(maxio, options, logger);
    }

    private static MaxioProductInfo ProPlan()
        => new("eshop-pro", "Pro Plan", "Monthly", 29900, 1, "month", "eshop-subscribe");

    private sealed class FakeMaxio : IMaxioAdvancedBillingClient
    {
        private int _nextCustomerId = 100;
        private int _nextSubscriptionId = 500;

        public List<MaxioProductInfo> Products { get; } = new();
        public Dictionary<string, MaxioCustomerInfo> Customers { get; } = new();
        public Dictionary<int, List<MaxioSubscriptionInfo>> Subscriptions { get; } = new();
        public int CreateCustomerCalls { get; private set; }
        public int CreateSubscriptionCalls { get; private set; }
        public int LastCreatedCustomerId { get; private set; }

        public Task<IReadOnlyList<MaxioProductInfo>> ListProductsForFamilyAsync(
            string productFamilyHandle, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MaxioProductInfo>>(Products);

        public Task<MaxioCustomerInfo?> FindCustomerByReferenceAsync(
            string reference, CancellationToken cancellationToken = default)
        {
            Customers.TryGetValue(reference, out var customer);
            return Task.FromResult(customer);
        }

        public Task<MaxioCustomerInfo> CreateCustomerAsync(
            string firstName, string lastName, string email, string reference,
            CancellationToken cancellationToken = default)
        {
            CreateCustomerCalls++;
            if (Customers.TryGetValue(reference, out var existing))
            {
                return Task.FromResult(existing);
            }

            var created = new MaxioCustomerInfo(_nextCustomerId++, reference);
            Customers[reference] = created;
            return Task.FromResult(created);
        }

        public Task<IReadOnlyList<MaxioSubscriptionInfo>> ListCustomerSubscriptionsAsync(
            int customerId, CancellationToken cancellationToken = default)
        {
            if (!Subscriptions.TryGetValue(customerId, out var list))
            {
                return Task.FromResult<IReadOnlyList<MaxioSubscriptionInfo>>(Array.Empty<MaxioSubscriptionInfo>());
            }

            return Task.FromResult<IReadOnlyList<MaxioSubscriptionInfo>>(list);
        }

        public Task<MaxioSubscriptionInfo> CreateSubscriptionAsync(
            int customerId, string productHandle, CancellationToken cancellationToken = default)
        {
            CreateSubscriptionCalls++;
            LastCreatedCustomerId = customerId;
            var product = Products.Single(p => p.Handle == productHandle);
            var created = new MaxioSubscriptionInfo(
                _nextSubscriptionId++,
                "active",
                product.Handle,
                product.Name,
                product.PriceInCents,
                DateTimeOffset.UtcNow.AddMonths(1));

            if (!Subscriptions.TryGetValue(customerId, out var list))
            {
                list = new List<MaxioSubscriptionInfo>();
                Subscriptions[customerId] = list;
            }

            list.Add(created);
            return Task.FromResult(created);
        }
    }
}
