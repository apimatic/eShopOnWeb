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
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointTests
{
    [TestMethod]
    public async Task SubscriptionPlansRequiresBearerToken()
    {
        await using var factory = new SubscriptionApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AuthenticatedShopperCanCompleteHeroFlow()
    {
        await using var factory = new SubscriptionApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());

        var plansResponse = await client.GetFromJsonAsync<ListSubscriptionPlansResponse>(
            "api/subscription-plans");
        Assert.AreEqual("eshop-pro", plansResponse!.SubscriptionPlans.Single().Handle);

        var createResponse = await client.PostAsJsonAsync("api/subscriptions", new
        {
            productHandle = "eshop-pro"
        });
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();
        Assert.IsTrue(created!.Created);
        Assert.AreEqual("active", created.Subscription.State);
        Assert.IsNotNull(created.Subscription.NextBillingAt);

        var subscriptions = await client.GetFromJsonAsync<ListMySubscriptionsResponse>(
            "api/my-subscriptions");
        Assert.AreEqual("eshop-pro", subscriptions!.Subscriptions.Single().ProductHandle);
        Assert.AreEqual(29900L, subscriptions.Subscriptions.Single().PriceInCents);
    }

    private sealed class SubscriptionApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISubscriptionBillingService>();
                services.AddSingleton<ISubscriptionBillingService, FakeSubscriptionBillingService>();
            });
        }
    }

    private sealed class FakeSubscriptionBillingService : ISubscriptionBillingService
    {
        private static readonly DateTimeOffset NextBillingAt = new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);

        public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlan>>(new[]
            {
                new SubscriptionPlan(1, "eshop-pro", "Pro Plan", null, 29900, 1, "month")
            });

        public Task<SubscribeResult> SubscribeAsync(
            SubscriptionShopper shopper,
            string productHandle,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SubscribeResult(CreateSubscription(), true));

        public Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsAsync(
            SubscriptionShopper shopper,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ShopperSubscription>>(new[] { CreateSubscription() });

        private static ShopperSubscription CreateSubscription() =>
            new(10, "eshop-pro", "Pro Plan", 29900, "USD", "active", NextBillingAt);
    }
}
