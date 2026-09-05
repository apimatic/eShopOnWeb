using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.FunctionalTests.PublicApi.SubscriptionEndpoints;

public class SubscriptionEnrollmentServiceTests
{
    [Fact]
    public async Task RepeatedEnrollmentForTheSamePlanReturnsTheExistingSubscription()
    {
        var maxio = new FakeMaxioBillingClient();
        var service = new SubscriptionEnrollmentService(maxio);
        var user = new ApplicationUser { Id = "shopper-1", Email = "shopper@example.test" };

        var created = await service.EnrollAsync(user, "pro", CancellationToken.None);
        var duplicate = await service.EnrollAsync(user, "pro", CancellationToken.None);

        Assert.False(created.AlreadySubscribed);
        Assert.True(duplicate.AlreadySubscribed);
        Assert.Equal(created.Subscription.Id, duplicate.Subscription.Id);
        Assert.Equal(1, maxio.CustomerCreates);
        Assert.Equal(1, maxio.SubscriptionCreates);
    }

    private sealed class FakeMaxioBillingClient : IMaxioBillingClient
    {
        private readonly MaxioProduct _plan = new() { Id = 1, Handle = "pro", Name = "Pro", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" };
        private MaxioCustomer? _customer;
        private readonly List<MaxioSubscription> _subscriptions = new();
        public int CustomerCreates { get; private set; }
        public int SubscriptionCreates { get; private set; }

        public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MaxioProduct>>(new[] { _plan });
        public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken) => Task.FromResult(_customer);
        public Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerDraft customer, string uniquenessToken, CancellationToken cancellationToken)
        {
            CustomerCreates++;
            _customer = new MaxioCustomer { Id = 42 };
            return Task.FromResult(_customer);
        }
        public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MaxioSubscription>>(_subscriptions.ToArray());
        public Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string reference, string uniquenessToken, CancellationToken cancellationToken)
        {
            SubscriptionCreates++;
            var subscription = new MaxioSubscription { Id = 99, State = "active", Product = _plan, ProductPriceInCents = _plan.PriceInCents };
            _subscriptions.Add(subscription);
            return Task.FromResult(subscription);
        }
    }
}
