using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointTests
{
    [TestMethod]
    public async Task EndpointsRequireBearerAuthentication()
    {
        await using var factory = CreateFactory(new FakeMaxioClient());
        using var client = factory.CreateClient();

        var plans = await client.GetAsync("/api/subscription-plans");
        var subscriptions = await client.GetAsync("/api/my-subscriptions");
        var subscribe = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "basic-plan" });

        Assert.AreEqual(401, (int)plans.StatusCode, await plans.Content.ReadAsStringAsync());
        Assert.AreEqual(401, (int)subscriptions.StatusCode, await subscriptions.Content.ReadAsStringAsync());
        Assert.AreEqual(401, (int)subscribe.StatusCode, await subscribe.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task ConcurrentSubscribeCreatesOneCustomerAndSubscriptionThenListsIt()
    {
        var maxio = new FakeMaxioClient();
        await using var factory = CreateFactory(maxio);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());

        var planHttpResponse = await client.GetAsync("/api/subscription-plans");
        Assert.AreEqual(200, (int)planHttpResponse.StatusCode, await planHttpResponse.Content.ReadAsStringAsync());
        var plansResponse = await planHttpResponse.Content.ReadFromJsonAsync<SubscriptionPlansResponse>();
        Assert.IsNotNull(plansResponse);
        Assert.AreEqual(1, plansResponse.Plans.Count);
        Assert.AreEqual("basic-plan", plansResponse.Plans[0].Handle);

        var firstRequest = client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "basic-plan" });
        var secondRequest = client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "basic-plan" });
        var responses = await Task.WhenAll(firstRequest, secondRequest);
        responses[0].EnsureSuccessStatusCode();
        responses[1].EnsureSuccessStatusCode();

        var first = await responses[0].Content.ReadFromJsonAsync<SubscriptionDto>();
        var second = await responses[1].Content.ReadFromJsonAsync<SubscriptionDto>();
        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual("active", first.State);
        Assert.AreEqual(2900L, first.PriceInCents);
        Assert.AreEqual(1, maxio.CustomerCreateCount);
        Assert.AreEqual(1, maxio.SubscriptionCreateCount);

        var mine = await client.GetFromJsonAsync<MySubscriptionsResponse>("/api/my-subscriptions");
        Assert.IsNotNull(mine);
        Assert.AreEqual(1, mine.Subscriptions.Count);
        Assert.AreEqual(first.Id, mine.Subscriptions[0].Id);
    }

    private static WebApplicationFactory<Program> CreateFactory(FakeMaxioClient maxio) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMaxioClient>();
                services.AddSingleton<IMaxioClient>(maxio);
            });
        });

    private sealed class FakeMaxioClient : IMaxioClient
    {
        private readonly ConcurrentDictionary<string, MaxioSubscription> _subscriptions = new();
        private MaxioCustomer? _customer;
        private int _customerCreateCount;
        private int _subscriptionCreateCount;

        public int CustomerCreateCount => _customerCreateCount;
        public int SubscriptionCreateCount => _subscriptionCreateCount;

        public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioProduct>>([Product]);

        public Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(_customer?.Reference == reference ? _customer : null);

        public Task<MaxioCustomer> CreateCustomerAsync(
            MaxioCreateCustomer customer,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _customerCreateCount);
            _customer = new MaxioCustomer
            {
                Id = 501,
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            };
            return Task.FromResult(_customer);
        }

        public Task<MaxioSubscription?> FindSubscriptionAsync(
            string reference,
            CancellationToken cancellationToken)
        {
            _subscriptions.TryGetValue(reference, out var subscription);
            return Task.FromResult(subscription);
        }

        public Task<MaxioSubscription?> ReadSubscriptionAsync(
            long subscriptionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_subscriptions.Values.SingleOrDefault(item => item.Id == subscriptionId));

        public async Task<MaxioSubscription> CreateSubscriptionAsync(
            MaxioCreateSubscription request,
            CancellationToken cancellationToken)
        {
            Assert.AreEqual("remittance", request.PaymentCollectionMethod);
            Interlocked.Increment(ref _subscriptionCreateCount);
            await Task.Delay(50, cancellationToken);
            var subscription = new MaxioSubscription
            {
                Id = 701,
                State = "active",
                ProductPriceInCents = 2900,
                CurrentPeriodEndsAt = DateTimeOffset.Parse("2026-09-21T00:00:00Z"),
                Reference = request.Reference,
                Customer = _customer!,
                Product = Product
            };
            _subscriptions[request.Reference] = subscription;
            return subscription;
        }

        public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
            long customerId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioSubscription>>(_subscriptions.Values.ToList());

        private static MaxioProduct Product => new()
        {
            Id = 101,
            Name = "Basic",
            Handle = "basic-plan",
            Description = "Basic plan",
            PriceInCents = 2900,
            Interval = 1,
            IntervalUnit = "month",
            RequireCreditCard = false,
            ProductFamily = new MaxioProductFamily
            {
                Id = 1,
                Name = "Plans",
                Handle = "test-family"
            }
        };
    }
}
