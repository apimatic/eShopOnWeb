using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests;

/// <summary>
/// Unit tests that pin down the hero-flow idempotency guarantee: a repeated (or concurrently double-clicked)
/// subscribe must never create a second Maxio customer or a second subscription.
/// </summary>
[TestClass]
public class SubscriptionServiceIdempotencyTests
{
    private static SubscriptionService CreateService(FakeMaxioClient client)
    {
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test",
            Subdomain = "test",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new SubscriptionService(client, new MaxioIdempotencyGuard(), settings, new NullAppLogger<SubscriptionService>());
    }

    [TestMethod]
    public async Task RepeatedSubscribe_DoesNotCreateDuplicateCustomerOrSubscription()
    {
        var client = new FakeMaxioClient();
        var service = CreateService(client);
        var user = new BillingUser("user-123", "shopper@example.com");

        var first = await service.SubscribeAsync(user, "eshop-pro");
        var second = await service.SubscribeAsync(user, "eshop-pro");

        Assert.IsFalse(first.AlreadyExisted, "First subscribe should create a new subscription.");
        Assert.IsTrue(second.AlreadyExisted, "Second subscribe should be an idempotent no-op.");
        Assert.AreEqual(first.Subscription.Id, second.Subscription.Id);
        Assert.AreEqual(1, client.CreateCustomerCount, "Only one Maxio customer should be created.");
        Assert.AreEqual(1, client.CreateSubscriptionCount, "Only one subscription should be created.");
    }

    [TestMethod]
    public async Task ConcurrentSubscribe_CreatesExactlyOneCustomerAndSubscription()
    {
        var client = new FakeMaxioClient();
        var service = CreateService(client);
        var user = new BillingUser("user-concurrent", "shopper@example.com");

        var tasks = Enumerable.Range(0, 12)
            .Select(_ => Task.Run(() => service.SubscribeAsync(user, "eshop-pro")))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.AreEqual(1, client.CreateCustomerCount, "Exactly one customer under concurrency.");
        Assert.AreEqual(1, client.CreateSubscriptionCount, "Exactly one subscription under concurrency.");
        Assert.AreEqual(1, results.Select(r => r.Subscription.Id).Distinct().Count(), "All callers see the same subscription.");
        Assert.AreEqual(1, results.Count(r => !r.AlreadyExisted), "Exactly one caller created it; the rest were no-ops.");
    }

    [TestMethod]
    public async Task Subscribe_DifferentPlans_CreatesTwoSubscriptionsForOneCustomer()
    {
        var client = new FakeMaxioClient();
        var service = CreateService(client);
        var user = new BillingUser("user-multi", "shopper@example.com");

        await service.SubscribeAsync(user, "eshop-pro");
        await service.SubscribeAsync(user, "basic-plan");

        Assert.AreEqual(1, client.CreateCustomerCount, "One customer shared across plans.");
        Assert.AreEqual(2, client.CreateSubscriptionCount, "Distinct plans yield distinct subscriptions.");
    }

    /// <summary>In-memory <see cref="IMaxioClient"/> that mimics Maxio's unique-reference customer semantics.</summary>
    private sealed class FakeMaxioClient : IMaxioClient
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, MaxioCustomer> _customersByReference = new(StringComparer.Ordinal);
        private readonly List<MaxioSubscription> _subscriptions = new();
        private int _nextCustomerId = 1000;
        private int _nextSubscriptionId = 5000;

        public int CreateCustomerCount { get; private set; }
        public int CreateSubscriptionCount { get; private set; }

        public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _customersByReference.TryGetValue(reference, out var customer);
                return Task.FromResult(customer);
            }
        }

        public Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes attributes, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (attributes.Reference != null && _customersByReference.ContainsKey(attributes.Reference))
                {
                    // Mimic Maxio rejecting a duplicate reference.
                    throw new MaxioApiException(System.Net.HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"Reference: has already been taken\"]}", "duplicate reference");
                }

                CreateCustomerCount++;
                var customer = new MaxioCustomer
                {
                    Id = _nextCustomerId++,
                    Reference = attributes.Reference,
                    Email = attributes.Email,
                    FirstName = attributes.FirstName,
                    LastName = attributes.LastName
                };
                if (attributes.Reference != null)
                {
                    _customersByReference[attributes.Reference] = customer;
                }
                return Task.FromResult(customer);
            }
        }

        public Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MaxioProduct>>(Array.Empty<MaxioProduct>());

        public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var subs = _subscriptions.Where(s => s.Customer?.Id == customerId).ToList();
                return Task.FromResult<IReadOnlyList<MaxioSubscription>>(subs);
            }
        }

        public Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionAttributes attributes, string uniquenessToken, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                CreateSubscriptionCount++;
                var customer = _customersByReference.Values.FirstOrDefault(c => c.Id == attributes.CustomerId);
                var subscription = new MaxioSubscription
                {
                    Id = _nextSubscriptionId++,
                    State = "active",
                    Product = new MaxioProduct { Handle = attributes.ProductHandle, Name = attributes.ProductHandle },
                    Customer = customer
                };
                _subscriptions.Add(subscription);
                return Task.FromResult(subscription);
            }
        }
    }

    private sealed class NullAppLogger<T> : IAppLogger<T>
    {
        public void LogInformation(string message, params object[] args) { }
        public void LogWarning(string message, params object[] args) { }
    }
}
