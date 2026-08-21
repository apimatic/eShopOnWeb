using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionServiceTests
{
    [TestMethod]
    public async Task RepeatedSubscribeReturnsSameMaxioSubscription()
    {
        var maxio = new StatefulMaxioClient();
        await using var dbContext = NewIdentityContext();
        var service = new SubscriptionService(maxio, dbContext, Options.Create(new MaxioOptions
        {
            ApiKey = "test",
            Subdomain = "test-site",
            ProductFamilyHandle = "family"
        }));
        var user = new BillingUser("user-1", "shopper@example.test", "shopper@example.test");

        var first = await service.SubscribeAsync(user, "pro", CancellationToken.None);
        var second = await service.SubscribeAsync(user, "pro", CancellationToken.None);

        Assert.IsTrue(first.Created);
        Assert.IsFalse(second.Created);
        Assert.AreEqual(first.Subscription.Id, second.Subscription.Id);
        Assert.AreEqual(1, maxio.CustomerCreateCount);
        Assert.AreEqual(1, maxio.SubscriptionCreateCount);
        Assert.AreEqual("remittance", maxio.LastPaymentCollectionMethod);
        Assert.AreEqual(1, await dbContext.MaxioCustomers.CountAsync());
        Assert.AreEqual(1, await dbContext.MaxioSubscriptions.CountAsync());
    }

    [TestMethod]
    public async Task LegacySiteUsesInvoiceCollectionWithoutCardCapture()
    {
        var maxio = new StatefulMaxioClient(relationshipInvoicingEnabled: false);
        await using var dbContext = NewIdentityContext();
        var service = new SubscriptionService(maxio, dbContext, Options.Create(new MaxioOptions
        {
            ApiKey = "test",
            Subdomain = "legacy-site",
            ProductFamilyHandle = "family"
        }));

        await service.SubscribeAsync(
            new BillingUser("user-2", "shopper@example.test", "shopper@example.test"),
            "pro",
            CancellationToken.None);

        Assert.AreEqual("invoice", maxio.LastPaymentCollectionMethod);
    }

    private static AppIdentityDbContext NewIdentityContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppIdentityDbContext(options);
    }

    private sealed class StatefulMaxioClient : IMaxioClient
    {
        private readonly bool _relationshipInvoicingEnabled;
        private MaxioCustomer? _customer;
        private MaxioSubscription? _subscription;

        public StatefulMaxioClient(bool relationshipInvoicingEnabled = true)
        {
            _relationshipInvoicingEnabled = relationshipInvoicingEnabled;
        }

        public int CustomerCreateCount { get; private set; }
        public int SubscriptionCreateCount { get; private set; }
        public string? LastPaymentCollectionMethod { get; private set; }

        public Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MaxioSite { RelationshipInvoicingEnabled = _relationshipInvoicingEnabled, Test = true });

        public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioProduct>>([Product()]);

        public Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(_customer);

        public Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, string uniquenessToken, CancellationToken cancellationToken)
        {
            CustomerCreateCount++;
            _customer = new MaxioCustomer { Id = 10, Reference = customer.Reference };
            return Task.FromResult(_customer);
        }

        public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioSubscription>>(_subscription is null ? [] : [_subscription]);

        public Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(_subscription);

        public Task<MaxioSubscription> CreateSubscriptionAsync(string customerReference, string productHandle, string subscriptionReference, string paymentCollectionMethod, string uniquenessToken, CancellationToken cancellationToken)
        {
            SubscriptionCreateCount++;
            LastPaymentCollectionMethod = paymentCollectionMethod;
            _subscription = new MaxioSubscription
            {
                Id = 20,
                State = "active",
                Reference = subscriptionReference,
                ProductPriceInCents = 29900,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
                Customer = _customer,
                Product = Product()
            };
            return Task.FromResult(_subscription);
        }

        private static MaxioProduct Product() => new()
        {
            Id = 7,
            Name = "Pro",
            Handle = "pro",
            PriceInCents = 29900,
            Interval = 1,
            IntervalUnit = "month"
        };
    }
}
