using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointTests
{
    [TestMethod]
    public async Task SubscriptionPlansRequireJwtAuthentication()
    {
        await using var factory = CreateFactory(new FakeMaxioGateway());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ConcurrentDuplicateSubscribeCreatesOneProviderSubscription()
    {
        var gateway = new FakeMaxioGateway();
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());

        var requests = new[]
        {
            client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "eshop-pro" }),
            client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "eshop-pro" })
        };
        var responses = await Task.WhenAll(requests);

        Assert.IsTrue(responses.All(x => x.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));
        Assert.AreEqual(1, gateway.CustomerCreateCount);
        Assert.AreEqual(1, gateway.SubscriptionCreateCount);

        var account = await client.GetAsync("/api/my-subscriptions");
        account.EnsureSuccessStatusCode();
        var subscriptions = await account.Content.ReadFromJsonAsync<BillingSubscription[]>();
        Assert.AreEqual(1, subscriptions!.Length);
        Assert.AreEqual("eshop-pro", subscriptions[0].ProductHandle);
    }

    private static WebApplicationFactory<Program> CreateFactory(FakeMaxioGateway gateway) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMaxioBillingGateway>();
                services.AddSingleton<IMaxioBillingGateway>(gateway);
            }));

    private sealed class FakeMaxioGateway : IMaxioBillingGateway
    {
        private readonly object _gate = new();
        private BillingCustomer? _customer;
        private BillingSubscription? _subscription;

        public int CustomerCreateCount { get; private set; }
        public int SubscriptionCreateCount { get; private set; }

        public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BillingPlan>>(
                [new BillingPlan("eshop-pro", "Pro", null, 29900, 1, "month", "default")]);

        public Task<BillingPlan?> FindPlanAsync(string productHandle, CancellationToken cancellationToken) =>
            Task.FromResult<BillingPlan?>(productHandle == "eshop-pro"
                ? new BillingPlan("eshop-pro", "Pro", null, 29900, 1, "month", "default")
                : null);

        public Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult(_customer);
            }
        }

        public Task<BillingCustomer> CreateCustomerAsync(
            string reference,
            string firstName,
            string lastName,
            string email,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                CustomerCreateCount++;
                _customer = new BillingCustomer(7, reference);
                return Task.FromResult(_customer);
            }
        }

        public Task<BillingSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult(_subscription);
            }
        }

        public Task<BillingSubscription> CreateSubscriptionAsync(
            string productHandle,
            string customerReference,
            string subscriptionReference,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                SubscriptionCreateCount++;
                _subscription = new BillingSubscription(
                    11,
                    subscriptionReference,
                    productHandle,
                    "Pro",
                    29900,
                    "USD",
                    "active",
                    DateTimeOffset.UtcNow.AddMonths(1));
                return Task.FromResult(_subscription);
            }
        }

        public Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(
            int customerId,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyList<BillingSubscription>>(
                    _subscription is null ? [] : [_subscription]);
            }
        }
    }
}
