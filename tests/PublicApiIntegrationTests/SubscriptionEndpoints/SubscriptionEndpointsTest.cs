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
public sealed class SubscriptionEndpointsTest
{
    private WebApplicationFactory<Program> _factory = null!;
    private FakeMaxioClient _maxio = null!;

    [TestInitialize]
    public void Initialize()
    {
        _maxio = new FakeMaxioClient();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMaxioClient>();
                services.AddSingleton<IMaxioClient>(_maxio);
            });
        });
    }

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    [TestMethod]
    public async Task EndpointsRequireJwtAuthentication()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeIsIdempotentAndAppearsInMySubscriptions()
    {
        using var client = CreateAuthorizedClient();

        var plansResponse = await client.GetAsync("/api/subscription-plans");
        plansResponse.EnsureSuccessStatusCode();
        var plans = await plansResponse.Content.ReadFromJsonAsync<SubscriptionPlansResponse>();
        Assert.AreEqual(1, plans!.Plans.Count);
        Assert.AreEqual("basic-plan", plans.Plans[0].Handle);
        Assert.AreEqual(29m, plans.Plans[0].Price);

        var first = client.PostAsJsonAsync("/api/subscriptions", new SubscribeRequest
        {
            ProductHandle = "basic-plan"
        });
        var second = client.PostAsJsonAsync("/api/subscriptions", new SubscribeRequest
        {
            ProductHandle = "basic-plan"
        });
        var responses = await Task.WhenAll(first, second);

        var responseDetails = await Task.WhenAll(responses.Select(async x =>
            $"{(int)x.StatusCode}: {await x.Content.ReadAsStringAsync()}"));
        Assert.IsTrue(
            responses.All(x => x.StatusCode == HttpStatusCode.Created),
            string.Join(Environment.NewLine, responseDetails));
        Assert.AreEqual(1, _maxio.CustomerCreateCount);
        Assert.AreEqual(1, _maxio.SubscriptionCreateCount);
        Assert.AreEqual("remittance", _maxio.LastPaymentCollectionMethod);

        var mineResponse = await client.GetAsync("/api/my-subscriptions");
        mineResponse.EnsureSuccessStatusCode();
        var mine = await mineResponse.Content.ReadFromJsonAsync<MySubscriptionsResponse>();
        Assert.AreEqual(1, mine!.Subscriptions.Count);
        Assert.AreEqual("active", mine.Subscriptions[0].State);
        Assert.AreEqual(29m, mine.Subscriptions[0].Price);
        Assert.IsNotNull(mine.Subscriptions[0].NextBillingAt);
    }

    [TestMethod]
    public async Task RejectsProductOutsideConfiguredFamily()
    {
        using var client = CreateAuthorizedClient();

        var response = await client.PostAsJsonAsync("/api/subscriptions", new SubscribeRequest
        {
            ProductHandle = "foreign-plan"
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, _maxio.SubscriptionCreateCount);
    }

    private HttpClient CreateAuthorizedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private sealed class FakeMaxioClient : IMaxioClient
    {
        private readonly object _gate = new();
        private MaxioCustomer? _customer;
        private readonly List<MaxioSubscription> _subscriptions = new();
        private int _customerCreateCount;
        private int _subscriptionCreateCount;

        private static readonly MaxioProduct BasicPlan = new()
        {
            Id = 10,
            Handle = "basic-plan",
            Name = "Basic Plan",
            Description = "Basic",
            PriceInCents = 2900,
            Interval = 1,
            IntervalUnit = "month",
            ProductFamily = new MaxioProductFamily { Handle = "integration-test-family" }
        };

        private static readonly MaxioProduct ForeignPlan = new()
        {
            Id = 11,
            Handle = "foreign-plan",
            Name = "Foreign Plan",
            PriceInCents = 100,
            Interval = 1,
            IntervalUnit = "month",
            ProductFamily = new MaxioProductFamily { Handle = "another-family" }
        };

        public int CustomerCreateCount => _customerCreateCount;
        public int SubscriptionCreateCount => _subscriptionCreateCount;
        public string? LastPaymentCollectionMethod { get; private set; }

        public Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MaxioSite { RelationshipInvoicingEnabled = true });

        public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioProduct>>(new[] { BasicPlan, ForeignPlan });

        public Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken) =>
            Task.FromResult<MaxioProduct?>(string.Equals(handle, BasicPlan.Handle, StringComparison.OrdinalIgnoreCase)
                ? BasicPlan
                : string.Equals(handle, ForeignPlan.Handle, StringComparison.OrdinalIgnoreCase)
                    ? ForeignPlan
                    : null);

        public Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult(_customer?.Reference == reference ? _customer : null);
            }
        }

        public Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomer customer, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _customer ??= new MaxioCustomer { Id = 100, Reference = customer.Reference };
                Interlocked.Increment(ref _customerCreateCount);
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
                var existing = _subscriptions.FirstOrDefault(x => x.Reference == subscription.SubscriptionReference);
                if (existing is not null) return Task.FromResult(existing);

                LastPaymentCollectionMethod = subscription.PaymentCollectionMethod;
                var created = new MaxioSubscription
                {
                    Id = 200,
                    Reference = subscription.SubscriptionReference,
                    State = "active",
                    ProductPriceInCents = BasicPlan.PriceInCents,
                    NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
                    Customer = _customer!,
                    Product = BasicPlan
                };
                _subscriptions.Add(created);
                Interlocked.Increment(ref _subscriptionCreateCount);
                return Task.FromResult(created);
            }
        }
    }
}
