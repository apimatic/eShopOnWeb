using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.PublicApi.AuthEndpoints;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    [TestMethod]
    public async Task RequireJwtAndReplayADoubleClickWithoutCreatingTwice()
    {
        var gateway = new FakeMaxioBillingGateway();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IMaxioBillingGateway>();
                    services.AddSingleton<IMaxioBillingGateway>(gateway);
                });
            });
        using var client = factory.CreateClient();

        var unauthorized = await client.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var auth = await client.PostAsJsonAsync(
            "api/authenticate",
            new AuthenticateRequest
            {
                Username = "demouser@microsoft.com",
                Password = AuthorizationConstants.DEFAULT_PASSWORD
            });
        auth.EnsureSuccessStatusCode();
        var authResponse = await auth.Content.ReadFromJsonAsync<AuthenticateResponse>();
        Assert.IsNotNull(authResponse);
        Assert.IsTrue(authResponse.Result);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authResponse.Token);

        var plansResponse = await client.GetAsync("api/subscription-plans");
        plansResponse.EnsureSuccessStatusCode();
        var plans = await plansResponse.Content.ReadFromJsonAsync<List<SubscriptionPlan>>();
        Assert.IsNotNull(plans);
        Assert.AreEqual(2, plans.Count);

        var request = new CreateSubscriptionRequest
        {
            ProductHandle = "eshop-pro",
            IdempotencyKey = "double-click-1"
        };
        var firstRequest = client.PostAsJsonAsync("api/subscriptions", request);
        var secondRequest = client.PostAsJsonAsync("api/subscriptions", request);
        await Task.WhenAll(firstRequest, secondRequest);
        var first = await firstRequest;
        var second = await secondRequest;
        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();

        Assert.AreEqual(1, gateway.CreateCount);
        var firstSubscription = await first.Content.ReadFromJsonAsync<UserSubscription>();
        var secondSubscription = await second.Content.ReadFromJsonAsync<UserSubscription>();
        Assert.AreEqual(firstSubscription, secondSubscription);

        var mine = await client.GetFromJsonAsync<List<UserSubscription>>("api/my-subscriptions");
        Assert.IsNotNull(mine);
        Assert.AreEqual(1, mine.Count);
        Assert.AreEqual("eshop-pro", mine.Single().ProductHandle);
    }

    private sealed class FakeMaxioBillingGateway : IMaxioBillingGateway
    {
        private readonly ConcurrentDictionary<string, UserSubscription> _subscriptions = new();
        private int _createCount;

        public int CreateCount => _createCount;

        public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlan>>(
            [
                new("basic-plan", "Basic", 2900, 1, "month"),
                new("eshop-pro", "Pro", 29900, 1, "month")
            ]);

        public Task<UserSubscription?> FindSubscriptionAsync(
            string reference,
            CancellationToken cancellationToken)
        {
            _subscriptions.TryGetValue(reference, out var subscription);
            return Task.FromResult(subscription);
        }

        public Task<UserSubscription> CreateSubscriptionAsync(
            BillingCustomer customer,
            string productHandle,
            string subscriptionReference,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createCount);
            var subscription = new UserSubscription(
                subscriptionReference,
                productHandle,
                productHandle == "eshop-pro" ? "Pro" : "Basic",
                productHandle == "eshop-pro" ? 29900 : 2900,
                "active",
                DateTimeOffset.Parse("2026-09-21T00:00:00Z"));
            _subscriptions[subscriptionReference] = subscription;
            return Task.FromResult(subscription);
        }

        public Task<IReadOnlyList<UserSubscription>> ListSubscriptionsAsync(
            string customerReference,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserSubscription>>(_subscriptions.Values.ToArray());
    }
}
