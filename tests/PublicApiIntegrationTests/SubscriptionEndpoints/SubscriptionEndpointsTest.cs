using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public sealed class SubscriptionEndpointsTest
{
    [TestMethod]
    public async Task RoutesRequireAJwt()
    {
        await using var factory = new SubscriptionTestApplication();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var plans = await client.GetAsync("api/subscription-plans");
        var subscriptions = await client.GetAsync("api/my-subscriptions");
        var subscribe = await client.PostAsJsonAsync("api/subscriptions", new SubscribeRequest
        {
            ProductHandle = "eshop-pro"
        });

        Assert.AreEqual(HttpStatusCode.Unauthorized, plans.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, subscriptions.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, subscribe.StatusCode);
    }

    [TestMethod]
    public async Task HeroFlowCreatesOnlyOneSubscriptionForConcurrentDoubleClick()
    {
        await using var factory = new SubscriptionTestApplication();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());

        var plansResponse = await client.GetAsync("api/subscription-plans");
        plansResponse.EnsureSuccessStatusCode();
        var plans = await plansResponse.Content.ReadFromJsonAsync<SubscriptionPlansResponse>();
        Assert.IsNotNull(plans);
        Assert.AreEqual(1, plans.Plans.Count);
        Assert.AreEqual("eshop-pro", plans.Plans[0].Handle);
        Assert.AreEqual(29900, plans.Plans[0].PriceInCents);

        var first = client.PostAsJsonAsync("api/subscriptions", new SubscribeRequest
        {
            ProductHandle = "eshop-pro"
        });
        var second = client.PostAsJsonAsync("api/subscriptions", new SubscribeRequest
        {
            ProductHandle = "eshop-pro"
        });
        var responses = await Task.WhenAll(first, second);

        var responseDetails = await Task.WhenAll(responses.Select(async response =>
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}"));
        Assert.IsTrue(
            responses.All(response => response.StatusCode == HttpStatusCode.Created),
            string.Join(Environment.NewLine, responseDetails));
        Assert.AreEqual(1, factory.Gateway.CreateSubscriptionCalls);

        var created = await responses[0].Content.ReadFromJsonAsync<SubscribeResponse>();
        Assert.IsNotNull(created);
        Assert.AreEqual("eshop-pro", created.Subscription.PlanHandle);
        Assert.AreEqual(29900, created.Subscription.PriceInCents);
        Assert.AreEqual("active", created.Subscription.State);
        Assert.IsNotNull(created.Subscription.NextBillingDate);

        var mineResponse = await client.GetAsync("api/my-subscriptions");
        mineResponse.EnsureSuccessStatusCode();
        var mine = await mineResponse.Content.ReadFromJsonAsync<MySubscriptionsResponse>();
        Assert.IsNotNull(mine);
        Assert.AreEqual(1, mine.Subscriptions.Count);
        Assert.AreEqual(created.Subscription.MaxioSubscriptionId, mine.Subscriptions[0].MaxioSubscriptionId);
    }

    private sealed class SubscriptionTestApplication : WebApplicationFactory<Program>
    {
        public FakeSubscriptionBillingGateway Gateway { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["UseOnlyInMemoryDatabase"] = "true",
                    ["Maxio:ApiKey"] = "subscription-test-key",
                    ["Maxio:Subdomain"] = "subscription-test",
                    ["Maxio:ProductFamilyHandle"] = "subscription-test-family",
                    ["Maxio:BaseUrl"] = "https://maxio.invalid"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISubscriptionBillingGateway>();
                services.AddSingleton<ISubscriptionBillingGateway>(Gateway);
            });
        }
    }

    private sealed class FakeSubscriptionBillingGateway : ISubscriptionBillingGateway
    {
        private readonly ConcurrentDictionary<string, CustomerSubscription> _subscriptions = new();
        private BillingCustomer? _customer;
        private int _createSubscriptionCalls;

        public int CreateSubscriptionCalls => Volatile.Read(ref _createSubscriptionCalls);

        public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SubscriptionPlan>>(new[] { Plan() });
        }

        public Task<SubscriptionPlan?> FindPlanAsync(string productHandle, CancellationToken cancellationToken)
        {
            return Task.FromResult<SubscriptionPlan?>(productHandle == "eshop-pro" ? Plan() : null);
        }

        public Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
        {
            return Task.FromResult(_customer);
        }

        public Task<BillingCustomer> CreateCustomerAsync(
            BillingUser user,
            string reference,
            CancellationToken cancellationToken)
        {
            _customer = new BillingCustomer(42, reference);
            return Task.FromResult(_customer);
        }

        public Task<CustomerSubscription?> FindSubscriptionAsync(
            string reference,
            CancellationToken cancellationToken)
        {
            _subscriptions.TryGetValue(reference, out var subscription);
            return Task.FromResult(subscription);
        }

        public async Task<CustomerSubscription> CreateSubscriptionAsync(
            string productHandle,
            int maxioCustomerId,
            string reference,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createSubscriptionCalls);
            await Task.Delay(100, cancellationToken);
            var subscription = new CustomerSubscription(
                99,
                reference,
                "Pro Plan",
                productHandle,
                29900,
                "active",
                DateTimeOffset.UtcNow.AddMonths(1));
            _subscriptions[reference] = subscription;
            return subscription;
        }

        public Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(
            int maxioCustomerId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CustomerSubscription>>(_subscriptions.Values.ToList());
        }

        private static SubscriptionPlan Plan() => new(
            7,
            "Pro Plan",
            "eshop-pro",
            "Pro subscription",
            29900,
            1,
            "month");
    }
}
