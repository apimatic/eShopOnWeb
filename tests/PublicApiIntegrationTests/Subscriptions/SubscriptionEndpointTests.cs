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
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Subscriptions;

[TestClass]
public class SubscriptionEndpointTests
{
    [TestMethod]
    public async Task AllSubscriptionRoutesRequireBearerToken()
    {
        await using var factory = CreateFactory(new FakeMaxioClient());
        using var client = factory.CreateClient();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("api/subscriptions", new { productHandle = "test-plan" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
    }

    [TestMethod]
    public async Task ConcurrentDoubleClickCreatesOneCustomerAndOneSubscription()
    {
        var maxio = new FakeMaxioClient();
        await using var factory = CreateFactory(maxio);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("api/subscriptions", new { productHandle = "test-plan" }),
            client.PostAsJsonAsync("api/subscriptions", new { productHandle = "test-plan" }));

        Assert.IsTrue(responses.All(response => response.StatusCode == HttpStatusCode.OK));
        Assert.AreEqual(1, maxio.CustomerCreateCount);
        Assert.AreEqual(1, maxio.SubscriptionCreateCount);

        var accountResponse = await client.GetAsync("api/my-subscriptions");
        accountResponse.EnsureSuccessStatusCode();
        var account = await accountResponse.Content.ReadFromJsonAsync<List<SubscriptionDto>>();
        Assert.IsNotNull(account);
        Assert.AreEqual(1, account.Count);
        Assert.AreEqual("test-plan", account[0].PlanHandle);
        Assert.AreEqual("active", account[0].State);
        Assert.IsNotNull(account[0].NextBillingAt);
    }

    private static WebApplicationFactory<Program> CreateFactory(IMaxioClient maxioClient) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMaxioClient>();
                services.AddSingleton(maxioClient);
            });
        });

    private sealed class FakeMaxioClient : IMaxioClient
    {
        private readonly object _sync = new();
        private MaxioCustomer? _customer;
        private MaxioSubscription? _subscription;
        private string _productFamilyHandle = string.Empty;

        public int CustomerCreateCount { get; private set; }
        public int SubscriptionCreateCount { get; private set; }

        public Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MaxioSite("USD", true, true));

        public Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken)
        {
            _productFamilyHandle = productFamilyHandle;
            return Task.FromResult<IReadOnlyList<MaxioProduct>>(new[]
            {
                new MaxioProduct(12, "test-plan", "Test Plan", "Test", 29900, 1, "month", false, null, productFamilyHandle)
            });
        }

        public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                return Task.FromResult(_customer);
            }
        }

        public Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomer customer, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                CustomerCreateCount++;
                _customer = new MaxioCustomer(101, customer.Reference);
                return Task.FromResult(_customer);
            }
        }

        public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                return Task.FromResult(_subscription);
            }
        }

        public Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscription subscription, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                SubscriptionCreateCount++;
                var product = new MaxioProduct(12, "test-plan", "Test Plan", "Test", 29900, 1, "month", false, null, _productFamilyHandle);
                _subscription = new MaxioSubscription(
                    201,
                    "active",
                    29900,
                    DateTimeOffset.UtcNow.AddMonths(1),
                    DateTimeOffset.UtcNow.AddMonths(1),
                    subscription.Reference,
                    "USD",
                    _customer!,
                    product);
                return Task.FromResult(_subscription);
            }
        }

        public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                return Task.FromResult<IReadOnlyList<MaxioSubscription>>(
                    _subscription is null ? Array.Empty<MaxioSubscription>() : new[] { _subscription });
            }
        }
    }
}
