using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Maxio;

[TestClass]
public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";

    private static MaxioOptions BuildOptions() => new()
    {
        ApiKey = "test-key",
        Subdomain = "cp-test",
        ProductFamilyHandle = FamilyHandle
    };

    private static MaxioSubscriptionService CreateService(FakeMaxioClient client) =>
        new(client, new MemoryCache(new MemoryCacheOptions()), Options.Create(BuildOptions()));

    private static ApplicationUser User(string email = "shopper@example.com") => new()
    {
        Id = "11111111-1111-1111-1111-111111111111",
        UserName = email,
        Email = email
    };

    [TestMethod]
    public async Task SubscribeTwiceToSamePlanCreatesSingleSubscription()
    {
        var client = new FakeMaxioClient();
        var service = CreateService(client);
        var user = User();

        var first = await service.SubscribeAsync(user, "eshop-pro", CancellationToken.None);
        var second = await service.SubscribeAsync(user, "eshop-pro", CancellationToken.None);

        Assert.AreEqual(first.Id, second.Id, "A repeated subscribe must return the existing subscription.");
        Assert.AreEqual(1, client.Subscriptions.Count(s => s.CustomerId == 1),
            "A double-click must never create two subscriptions.");
        Assert.AreEqual(1, client.CreateSubscriptionCalls);
    }

    [TestMethod]
    public async Task SubscribingToTwoDifferentPlansCreatesTwoSubscriptions()
    {
        var client = new FakeMaxioClient();
        var service = CreateService(client);
        var user = User();

        var pro = await service.SubscribeAsync(user, "eshop-pro", CancellationToken.None);
        var basic = await service.SubscribeAsync(user, "basic-plan", CancellationToken.None);

        Assert.AreNotEqual(pro.Id, basic.Id);
        Assert.AreEqual(2, client.Subscriptions.Count(s => s.CustomerId == 1));
    }

    [TestMethod]
    public async Task EnsureCustomerIsIdempotentAcrossCalls()
    {
        var client = new FakeMaxioClient();
        var service = CreateService(client);
        var user = User();

        await service.SubscribeAsync(user, "eshop-pro", CancellationToken.None);
        await service.SubscribeAsync(user, "basic-plan", CancellationToken.None);

        Assert.AreEqual(1, client.CreateCustomerCalls, "Only one Maxio customer may exist per eShop user.");
        Assert.AreEqual(1, client.Customers.Count);
    }

    [TestMethod]
    public async Task UnknownPlanIsRejected()
    {
        var client = new FakeMaxioClient();
        var service = CreateService(client);

        await Assert.ThrowsExceptionAsync<SubscriptionPlanNotFoundException>(() =>
            service.SubscribeAsync(User(), "does-not-exist", CancellationToken.None));
    }

    [TestMethod]
    public async Task GetPlansReturnsConfiguredFamilyProducts()
    {
        var client = new FakeMaxioClient();
        var service = CreateService(client);

        var plans = await service.GetPlansAsync(CancellationToken.None);

        Assert.AreEqual(2, plans.Count);
        Assert.AreEqual("basic-plan", plans[0].Handle, "Plans should be ordered by price (lowest first).");
        Assert.AreEqual("eshop-pro", plans[1].Handle);
    }

    [TestMethod]
    public async Task NewUserHasNoSubscriptions()
    {
        var client = new FakeMaxioClient();
        var service = CreateService(client);

        var subscriptions = await service.GetSubscriptionsAsync(User(), CancellationToken.None);

        Assert.AreEqual(0, subscriptions.Count);
    }

    /// <summary>
    /// In-memory stand-in for the Maxio API that reproduces its idempotency semantics:
    /// unique customer/subscription references are enforced with a 422 + reference error,
    /// which the service interprets as "a concurrent identical request already won".
    /// </summary>
    internal sealed class FakeMaxioClient : IMaxioApiClient
    {
        private long _nextCustomerId = 1;
        private long _nextSubscriptionId = 1;

        public List<MaxioCustomer> Customers { get; } = new();
        public List<FakeSubscription> Subscriptions { get; } = new();
        public int CreateCustomerCalls { get; private set; }
        public int CreateSubscriptionCalls { get; private set; }

        public sealed class FakeSubscription
        {
            public long Id { get; set; }
            public long CustomerId { get; set; }
            public string? Reference { get; set; }
            public string Handle { get; set; } = string.Empty;
        }

        public Task<IReadOnlyList<ProductFamilyDto>> ListProductFamiliesAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<ProductFamilyDto> result = new List<ProductFamilyDto>
            {
                new() { Id = 1, Handle = FamilyHandle, Name = "eShopSubscribe" }
            };
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(long productFamilyId, CancellationToken cancellationToken)
        {
            IReadOnlyList<MaxioProduct> result = new List<MaxioProduct>
            {
                new() { Id = 12, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" },
                new() { Id = 11, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
            };
            return Task.FromResult(result);
        }

        public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
        {
            var match = Customers.FirstOrDefault(c => c.Reference == reference);
            return Task.FromResult(match);
        }

        public Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken)
        {
            CreateCustomerCalls++;
            if (Customers.Any(c => c.Reference == request.Customer.Reference))
            {
                throw ReferenceConflict();
            }

            var customer = new MaxioCustomer
            {
                Id = _nextCustomerId++,
                FirstName = request.Customer.FirstName,
                LastName = request.Customer.LastName,
                Email = request.Customer.Email,
                Reference = request.Customer.Reference
            };
            Customers.Add(customer);
            return Task.FromResult(customer);
        }

        public Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(long customerId, CancellationToken cancellationToken)
        {
            IReadOnlyList<MaxioSubscription> result = Subscriptions
                .Where(s => s.CustomerId == customerId)
                .Select(s => BuildSubscription(s))
                .ToList();
            return Task.FromResult(result);
        }

        public Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken)
        {
            CreateSubscriptionCalls++;
            var product = new List<MaxioProduct>
            {
                new() { Id = 12, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" },
                new() { Id = 11, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
            }.First(p => p.Handle == request.Subscription.ProductHandle);

            if (Subscriptions.Any(s => s.Reference == request.Subscription.Reference))
            {
                throw ReferenceConflict();
            }

            var created = new FakeSubscription
            {
                Id = _nextSubscriptionId++,
                CustomerId = request.Subscription.CustomerId!.Value,
                Reference = request.Subscription.Reference,
                Handle = product.Handle
            };
            Subscriptions.Add(created);
            return Task.FromResult(BuildSubscription(created, product));
        }

        private static MaxioSubscription BuildSubscription(FakeSubscription fake, MaxioProduct? product = null)
        {
            product ??= new MaxioProduct { Id = 11, Handle = fake.Handle, Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" };
            return new MaxioSubscription
            {
                Id = fake.Id,
                State = "active",
                Reference = fake.Reference,
                BalanceInCents = product.PriceInCents,
                Product = product
            };
        }

        private static MaxioApiException ReferenceConflict() =>
            new(HttpStatusCode.UnprocessableEntity, new[] { "Reference: must be unique - that value has been taken." }, null);
    }
}
