using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi;

public class SubscriptionServiceTests
{
    [Fact]
    public async Task SubscribeTwiceReturnsOneMaxioSubscription()
    {
        var maxio = new FakeMaxioBillingClient();
        var service = new SubscriptionService(maxio, new UserSubscriptionCoordinator());
        var user = new ApplicationUser
        {
            Id = "shopper-1",
            UserName = "shopper@example.test",
            Email = "shopper@example.test"
        };

        var results = await Task.WhenAll(
            service.SubscribeAsync(user, "pro", CancellationToken.None),
            service.SubscribeAsync(user, "pro", CancellationToken.None));
        var first = results[0];
        var second = results[1];

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, maxio.CreateCustomerCalls);
        Assert.Equal(1, maxio.CreateSubscriptionCalls);

        var mine = await service.ListMySubscriptionsAsync(user, CancellationToken.None);
        Assert.Single(mine);
        Assert.Equal("pro", mine[0].PlanHandle);
    }

    private sealed class FakeMaxioBillingClient : IMaxioBillingClient
    {
        private readonly List<MaxioSubscription> _subscriptions = new();
        private MaxioCustomer? _customer;

        public int CreateCustomerCalls { get; private set; }
        public int CreateSubscriptionCalls { get; private set; }

        public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioProduct>>(new[]
            {
                new MaxioProduct(10, "Pro", "pro", null, 29900, 1, "month", null)
            });

        public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(_customer?.Reference == reference ? _customer : null);

        public Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
        {
            CreateCustomerCalls++;
            _customer = new MaxioCustomer(99, request.Customer.Email, request.Customer.Reference);
            return Task.FromResult(_customer);
        }

        public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioSubscription>>(_subscriptions.ToList());

        public Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken)
        {
            CreateSubscriptionCalls++;
            var subscription = new MaxioSubscription(
                123,
                "active",
                request.Subscription.Reference,
                29900,
                DateTimeOffset.Parse("2030-01-01T00:00:00Z"),
                new MaxioSubscriptionProduct("Pro", request.Subscription.ProductHandle, 1, "month"));
            _subscriptions.Add(subscription);
            return Task.FromResult(subscription);
        }
    }
}
