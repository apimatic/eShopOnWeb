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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointTests
{
    [TestMethod]
    public async Task AllSubscriptionRoutesRequireJwtAuthentication()
    {
        await using var application = CreateApplication(new FakeMaxioClient());
        var client = application.CreateClient();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("api/subscriptions", new { productHandle = "eshop-pro" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
    }

    [TestMethod]
    public async Task SubscribeIsIdempotentAndIsReflectedInMySubscriptions()
    {
        var maxio = new FakeMaxioClient();
        await using var application = CreateApplication(maxio);
        var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var plansResponse = await client.GetFromJsonAsync<SubscriptionPlansResponse>("api/subscription-plans");
        Assert.AreEqual(2, plansResponse!.Plans.Count);

        var firstRequest = client.PostAsJsonAsync("api/subscriptions", new { productHandle = "eshop-pro" });
        var secondRequest = client.PostAsJsonAsync("api/subscriptions", new { productHandle = "eshop-pro" });
        var responses = await Task.WhenAll(firstRequest, secondRequest);

        Assert.IsTrue(responses.All(response => response.StatusCode == HttpStatusCode.OK));
        Assert.AreEqual(1, maxio.CustomerCreateCount);
        Assert.AreEqual(1, maxio.SubscriptionCreateCount);

        var account = await client.GetFromJsonAsync<MySubscriptionsResponse>("api/my-subscriptions");
        Assert.AreEqual(1, account!.Subscriptions.Count);
        Assert.AreEqual("eshop-pro", account.Subscriptions[0].ProductHandle);
        Assert.AreEqual("active", account.Subscriptions[0].State);
        Assert.AreEqual(29900, account.Subscriptions[0].PriceInCents);
        Assert.IsNotNull(account.Subscriptions[0].NextBillingAt);
    }

    private static WebApplicationFactory<Program> CreateApplication(IMaxioClient maxioClient)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMaxioClient>();
                services.AddSingleton(maxioClient);
            });
        });
    }

    private sealed class FakeMaxioClient : IMaxioClient
    {
        private readonly IReadOnlyList<MaxioProduct> _products = new[]
        {
            Product("basic-plan", "Basic Plan", 2900),
            Product("eshop-pro", "Pro Plan", 29900)
        };
        private readonly ConcurrentDictionary<string, MaxioCustomer> _customers = new();
        private readonly ConcurrentDictionary<string, MaxioSubscription> _subscriptions = new();
        private int _customerCreateCount;
        private int _subscriptionCreateCount;

        public int CustomerCreateCount => _customerCreateCount;
        public int SubscriptionCreateCount => _subscriptionCreateCount;

        public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_products);

        public Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
        {
            _customers.TryGetValue(reference, out var customer);
            return Task.FromResult(customer);
        }

        public Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomer customer, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _customerCreateCount);
            var result = new MaxioCustomer { Id = 101, Email = customer.Email, Reference = customer.Reference };
            _customers[customer.Reference] = result;
            return Task.FromResult(result);
        }

        public Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
        {
            _subscriptions.TryGetValue(reference, out var subscription);
            return Task.FromResult(subscription);
        }

        public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
        {
            IReadOnlyList<MaxioSubscription> result = _subscriptions.Values
                .Where(subscription => subscription.Customer.Id == customerId)
                .ToList();
            return Task.FromResult(result);
        }

        public Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscription subscription, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _subscriptionCreateCount);
            var customer = _customers[subscription.CustomerReference];
            var product = _products.Single(item => item.Handle == subscription.ProductHandle);
            var result = new MaxioSubscription
            {
                Id = 202,
                State = "active",
                Reference = subscription.Reference,
                ProductPriceInCents = product.PriceInCents,
                ProductPricePointName = "Default",
                NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
                Customer = customer,
                Product = product
            };
            _subscriptions[subscription.Reference] = result;
            return Task.FromResult(result);
        }

        private static MaxioProduct Product(string handle, string name, long price) => new()
        {
            Id = price,
            Handle = handle,
            Name = name,
            Description = name,
            PriceInCents = price,
            Interval = 1,
            IntervalUnit = "month",
            ProductPricePointName = "Default"
        };
    }
}
