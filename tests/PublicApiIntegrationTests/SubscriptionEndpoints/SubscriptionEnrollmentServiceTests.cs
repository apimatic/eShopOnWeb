using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEnrollmentServiceTests
{
    [TestMethod]
    public async Task SubscribeTwiceReturnsTheSameActiveSubscription()
    {
        var client = new FakeMaxioBillingClient();
        var service = new SubscriptionEnrollmentService(client);
        var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName = "shopper@example.test", Email = "shopper@example.test" };

        var first = await service.SubscribeAsync(user, "pro", CancellationToken.None);
        var second = await service.SubscribeAsync(user, "pro", CancellationToken.None);

        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(1, client.CreateCustomerCalls);
        Assert.AreEqual(1, client.CreateSubscriptionCalls);
        Assert.AreEqual("active", first.State);
        Assert.AreEqual(29900L, first.PriceInCents);
    }

    private sealed class FakeMaxioBillingClient : IMaxioBillingClient
    {
        private readonly List<MaxioSubscription> _subscriptions = new();
        private MaxioCustomer? _customer;

        public int CreateCustomerCalls { get; private set; }
        public int CreateSubscriptionCalls { get; private set; }

        public Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlanDto>>(new[]
            {
                new SubscriptionPlanDto { Handle = "pro", Name = "Pro", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
            });

        public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(_customer);

        public Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, string uniquenessToken, CancellationToken cancellationToken)
        {
            CreateCustomerCalls++;
            _customer = new MaxioCustomer { Id = 42, Reference = customer.Reference };
            return Task.FromResult(_customer);
        }

        public Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioSubscription>>(_subscriptions);

        public Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string planHandle, string uniquenessToken, CancellationToken cancellationToken)
        {
            CreateSubscriptionCalls++;
            var subscription = new MaxioSubscription
            {
                Id = 99,
                State = "active",
                ProductPriceInCents = 29900,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
                Product = new MaxioProduct { Handle = planHandle, Name = "Pro" }
            };
            _subscriptions.Add(subscription);
            return Task.FromResult(subscription);
        }
    }
}
