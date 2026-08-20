using System;
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
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public sealed class SubscriptionEndpointsTest : IDisposable
{
    private readonly FakeMaxioClient _maxio = new();
    private readonly WebApplicationFactory<Program> _application;
    private readonly HttpClient _client;

    public SubscriptionEndpointsTest()
    {
        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMaxioClient>();
                services.AddSingleton<IMaxioClient>(_maxio);
            });
        });
        _client = _application.CreateClient();
    }

    [TestMethod]
    public async Task RequiresBearerAuthentication()
    {
        var response = await _client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsPlansAndCreatesOnlyOneSubscriptionForConcurrentRequests()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());

        var plansResponse = await _client.GetAsync("api/subscription-plans");
        plansResponse.EnsureSuccessStatusCode();
        var plans = await plansResponse.Content.ReadFromJsonAsync<ListSubscriptionPlansResponse>();
        Assert.AreEqual(2, plans!.Plans.Count);
        Assert.AreEqual("basic-plan", plans.Plans[0].Handle);
        Assert.AreEqual(29m, plans.Plans[0].Price);

        var requests = Enumerable.Range(0, 2)
            .Select(_ => _client.PostAsJsonAsync(
                "api/subscriptions",
                new CreateSubscriptionRequest { ProductHandle = "eshop-pro" }))
            .ToArray();
        var responses = await Task.WhenAll(requests);

        Assert.IsTrue(responses.All(x => x.StatusCode == HttpStatusCode.Created));
        Assert.AreEqual(1, _maxio.CreateCustomerCalls);
        Assert.AreEqual(1, _maxio.CreateSubscriptionCalls);

        var mineResponse = await _client.GetAsync("api/my-subscriptions");
        mineResponse.EnsureSuccessStatusCode();
        var mine = await mineResponse.Content.ReadFromJsonAsync<ListMySubscriptionsResponse>();
        Assert.AreEqual(1, mine!.Subscriptions.Count);
        Assert.AreEqual("active", mine.Subscriptions[0].State);
        Assert.AreEqual(299m, mine.Subscriptions[0].Price);
        Assert.IsNotNull(mine.Subscriptions[0].NextBillingAt);
    }

    public void Dispose()
    {
        _client.Dispose();
        _application.Dispose();
    }

    private sealed class FakeMaxioClient : IMaxioClient
    {
        private readonly object _gate = new();
        private MaxioCustomer? _customer;
        private readonly List<MaxioSubscription> _subscriptions = new();
        private int _createCustomerCalls;
        private int _createSubscriptionCalls;

        public int CreateCustomerCalls => Volatile.Read(ref _createCustomerCalls);
        public int CreateSubscriptionCalls => Volatile.Read(ref _createSubscriptionCalls);

        public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioProduct>>(new[]
            {
                Product("eshop-pro", "Pro Plan", 29900),
                Product("basic-plan", "Basic Plan", 2900)
            });

        public Task<MaxioCustomer?> FindCustomerByReferenceAsync(
            string reference,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult(_customer);
            }
        }

        public Task<MaxioCustomer> CreateCustomerAsync(
            CreateMaxioCustomer customer,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _customer ??= new MaxioCustomer
                {
                    Id = 101,
                    Email = customer.Email,
                    Reference = customer.Reference
                };
                Interlocked.Increment(ref _createCustomerCalls);
                return Task.FromResult(_customer);
            }
        }

        public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
            long customerId,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyList<MaxioSubscription>>(_subscriptions.ToList());
            }
        }

        public Task<MaxioSubscription> CreateSubscriptionAsync(
            CreateMaxioSubscription subscription,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Interlocked.Increment(ref _createSubscriptionCalls);
                var product = subscription.ProductHandle == "eshop-pro"
                    ? Product("eshop-pro", "Pro Plan", 29900)
                    : Product("basic-plan", "Basic Plan", 2900);
                var created = new MaxioSubscription
                {
                    Id = 202,
                    State = "active",
                    ProductPriceInCents = product.PriceInCents,
                    Currency = "USD",
                    NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
                    CreatedAt = DateTimeOffset.UtcNow,
                    Product = product
                };
                _subscriptions.Add(created);
                return Task.FromResult(created);
            }
        }

        private static MaxioProduct Product(string handle, string name, long priceInCents) => new()
        {
            Id = handle == "eshop-pro" ? 1 : 2,
            Handle = handle,
            Name = name,
            Description = $"{name} description",
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = "month",
            ProductFamily = new MaxioProductFamily
            {
                Id = 10,
                Handle = "integration-test-family",
                Name = "Subscriptions"
            }
        };
    }
}
