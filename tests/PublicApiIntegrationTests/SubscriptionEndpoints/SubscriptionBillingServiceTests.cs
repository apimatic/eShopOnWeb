using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionBillingServiceTests
{
    [TestMethod]
    public async Task SubscribeIsIdempotentForTheSameUserAndProduct()
    {
        var maxio = new FakeMaxioClient();
        var service = CreateService(maxio);
        var user = new ApplicationUser
        {
            Id = "user-123",
            UserName = "shopper@example.com",
            Email = "shopper@example.com"
        };

        var results = await Task.WhenAll(
            service.SubscribeAsync(user, "eshop-pro", CancellationToken.None),
            service.SubscribeAsync(user, "eshop-pro", CancellationToken.None));

        Assert.AreEqual(1, maxio.CustomerCreateCount);
        Assert.AreEqual(1, maxio.SubscriptionCreateCount);
        Assert.AreEqual(results[0]!.Subscription.Id, results[1]!.Subscription.Id);
        Assert.AreEqual(1, results.Count(result => result!.Created));
        Assert.AreEqual("remittance", maxio.LastPaymentCollectionMethod);
    }

    [TestMethod]
    public async Task UsesInvoiceCollectionForLegacyStatementSite()
    {
        var maxio = new FakeMaxioClient { RelationshipInvoicingEnabled = false };
        var user = new ApplicationUser { Id = "legacy-user", Email = "legacy@example.com" };

        await CreateService(maxio).SubscribeAsync(user, "eshop-pro", CancellationToken.None);

        Assert.AreEqual("invoice", maxio.LastPaymentCollectionMethod);
    }

    [TestMethod]
    public async Task PlansExcludeArchivedProductsAndExposeBillingTerms()
    {
        var maxio = new FakeMaxioClient();
        maxio.Products.Add(new MaxioProduct
        {
            Name = "Archived",
            Handle = "archived",
            ArchivedAt = DateTimeOffset.UtcNow
        });

        var plans = await CreateService(maxio).GetPlansAsync(CancellationToken.None);

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("eshop-pro", plans[0].Handle);
        Assert.AreEqual(29900L, plans[0].PriceInCents);
        Assert.AreEqual("month", plans[0].IntervalUnit);
    }

    [TestMethod]
    public async Task MySubscriptionsDoesNotCreateCustomerWhenNoneExists()
    {
        var maxio = new FakeMaxioClient { Customer = null };
        var user = new ApplicationUser { Id = "new-user", Email = "new@example.com" };

        var subscriptions = await CreateService(maxio).GetSubscriptionsAsync(user, CancellationToken.None);

        Assert.AreEqual(0, subscriptions.Count);
        Assert.AreEqual(0, maxio.CustomerCreateCount);
    }

    private static SubscriptionBillingService CreateService(FakeMaxioClient maxio) => new(
        maxio,
        Options.Create(new MaxioOptions { ProductFamilyHandle = "test-family" }));

    private sealed class FakeMaxioClient : IMaxioClient
    {
        private MaxioSubscription? _subscription;

        public List<MaxioProduct> Products { get; } = new()
        {
            new MaxioProduct
            {
                Id = 99,
                Name = "Pro Plan",
                Handle = "eshop-pro",
                Description = "Pro",
                PriceInCents = 29900,
                Interval = 1,
                IntervalUnit = "month"
            }
        };

        public MaxioCustomer? Customer { get; set; }
        public int CustomerCreateCount { get; private set; }
        public int SubscriptionCreateCount { get; private set; }
        public bool RelationshipInvoicingEnabled { get; set; } = true;
        public string? LastPaymentCollectionMethod { get; private set; }

        public Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MaxioSite { RelationshipInvoicingEnabled = RelationshipInvoicingEnabled });

        public Task<IReadOnlyList<MaxioProduct>> GetProductsAsync(string productFamilyHandle, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioProduct>>(Products);

        public Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(Customer);

        public Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer, string uniquenessToken, CancellationToken cancellationToken)
        {
            CustomerCreateCount++;
            Customer = new MaxioCustomer { Id = 42, Reference = customer.Reference };
            return Task.FromResult(Customer);
        }

        public Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(_subscription);

        public Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreate subscription, string uniquenessToken, CancellationToken cancellationToken)
        {
            SubscriptionCreateCount++;
            LastPaymentCollectionMethod = subscription.PaymentCollectionMethod;
            _subscription = new MaxioSubscription
            {
                Id = 123,
                State = "active",
                Reference = subscription.Reference,
                ProductPriceInCents = 29900,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
                Product = Products[0]
            };
            return Task.FromResult(_subscription);
        }

        public Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioSubscription>>(_subscription is null
                ? Array.Empty<MaxioSubscription>()
                : new[] { _subscription });
    }
}
