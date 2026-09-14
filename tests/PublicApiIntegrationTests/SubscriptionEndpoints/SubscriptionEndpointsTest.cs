using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private WebApplicationFactory<Program> _application = null!;
    private FakeMaxioClient _maxio = null!;

    [TestInitialize]
    public void Initialize()
    {
        _maxio = new FakeMaxioClient();
        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMaxioClient>();
                services.AddSingleton<IMaxioClient>(_maxio);
            });
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        _application.Dispose();
    }

    [TestMethod]
    public async Task SubscriptionPlansRequireAuthentication()
    {
        using var client = _application.CreateClient();
        var response = await client.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeIsIdempotentAndAppearsInMySubscriptions()
    {
        using var client = CreateAuthenticatedClient();

        var plansResponse = await client.GetAsync("api/subscription-plans");
        plansResponse.EnsureSuccessStatusCode();
        var plans = await plansResponse.Content.ReadFromJsonAsync<SubscriptionPlansResponse>();
        Assert.AreEqual("eshop-pro", plans!.Plans.Single().Handle);
        Assert.AreEqual(29900, plans.Plans.Single().PriceInCents);

        var firstRequest = client.PostAsJsonAsync(
            "api/subscriptions",
            new CreateSubscriptionRequest { ProductHandle = "eshop-pro" });
        var secondRequest = client.PostAsJsonAsync(
            "api/subscriptions",
            new CreateSubscriptionRequest { ProductHandle = "eshop-pro" });

        var responses = await Task.WhenAll(firstRequest, secondRequest);
        foreach (var response in responses)
        {
            response.EnsureSuccessStatusCode();
        }

        var first = await responses[0].Content.ReadFromJsonAsync<SubscriptionDto>();
        var second = await responses[1].Content.ReadFromJsonAsync<SubscriptionDto>();
        Assert.AreEqual(first!.SubscriptionId, second!.SubscriptionId);
        Assert.AreEqual("active", first.State);
        Assert.AreEqual(299m, first.Price);
        Assert.IsNotNull(first.NextBillingAt);
        Assert.AreEqual(1, _maxio.CreateCustomerCallCount);
        Assert.AreEqual(1, _maxio.CreateSubscriptionCallCount);

        var mineResponse = await client.GetAsync("api/my-subscriptions");
        mineResponse.EnsureSuccessStatusCode();
        var mine = await mineResponse.Content.ReadFromJsonAsync<MySubscriptionsResponse>();
        Assert.AreEqual(1, mine!.Subscriptions.Count);
        Assert.AreEqual(first.SubscriptionId, mine.Subscriptions.Single().SubscriptionId);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private sealed class FakeMaxioClient : IMaxioClient
    {
        private readonly ConcurrentDictionary<string, MaxioSubscription> _subscriptions = new();
        private MaxioCustomer? _customer;
        private int _createCustomerCallCount;
        private int _createSubscriptionCallCount;

        public int CreateCustomerCallCount => _createCustomerCallCount;
        public int CreateSubscriptionCallCount => _createSubscriptionCallCount;

        public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(
            string productFamilyHandle,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<MaxioProduct> products = new[]
            {
                new MaxioProduct
                {
                    Id = 101,
                    Handle = "eshop-pro",
                    Name = "Pro Plan",
                    Description = "Pro",
                    PriceInCents = 29900,
                    Interval = 1,
                    IntervalUnit = "month",
                    RequireCreditCard = false,
                    ProductFamily = new MaxioProductFamily { Id = 10, Handle = productFamilyHandle }
                }
            };
            return Task.FromResult(products);
        }

        public Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
        {
            return Task.FromResult(_customer?.Reference == reference ? _customer : null);
        }

        public Task<MaxioCustomer> CreateCustomerAsync(
            MaxioCustomerDetails customer,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createCustomerCallCount);
            _customer = new MaxioCustomer { Id = 201, Reference = customer.Reference };
            return Task.FromResult(_customer);
        }

        public Task<MaxioSubscription?> FindSubscriptionAsync(
            string reference,
            CancellationToken cancellationToken)
        {
            _subscriptions.TryGetValue(reference, out var subscription);
            return Task.FromResult(subscription);
        }

        public async Task<MaxioSubscription> CreateSubscriptionAsync(
            MaxioSubscriptionDetails subscription,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createSubscriptionCallCount);
            await Task.Delay(50, cancellationToken);
            return _subscriptions.GetOrAdd(subscription.Reference, _ => CreateSubscription(subscription));
        }

        public Task<MaxioSubscription?> ReadSubscriptionAsync(
            int subscriptionId,
            CancellationToken cancellationToken)
        {
            var subscription = _subscriptions.Values.SingleOrDefault(candidate => candidate.Id == subscriptionId);
            return Task.FromResult(subscription);
        }

        public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
            int customerId,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<MaxioSubscription> subscriptions = _subscriptions.Values
                .Where(subscription => subscription.Customer.Id == customerId)
                .ToList();
            return Task.FromResult(subscriptions);
        }

        private static MaxioSubscription CreateSubscription(MaxioSubscriptionDetails details)
        {
            return new MaxioSubscription
            {
                Id = 301,
                State = "active",
                ProductPriceInCents = 29900,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
                Reference = details.Reference,
                Customer = new MaxioCustomer { Id = details.CustomerId },
                Product = new MaxioProduct
                {
                    Id = 101,
                    Handle = details.ProductHandle,
                    Name = "Pro Plan",
                    PriceInCents = 29900,
                    Interval = 1,
                    IntervalUnit = "month"
                }
            };
        }
    }
}
