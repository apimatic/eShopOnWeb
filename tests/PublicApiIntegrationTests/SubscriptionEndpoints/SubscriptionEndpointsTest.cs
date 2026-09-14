using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public sealed class SubscriptionEndpointsTest
{
    [TestMethod]
    public async Task AllSubscriptionEndpointsRequireBearerToken()
    {
        await using var factory = new SubscriptionApiFactory();
        using var client = factory.CreateClient();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(
            "api/subscriptions",
            new { productHandle = "pro" })).StatusCode);
    }

    [TestMethod]
    public async Task AuthenticatedShopperCanBrowseSubscribeAndListAccountSubscriptions()
    {
        await using var factory = new SubscriptionApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());

        var plansResponse = await client.GetAsync("api/subscription-plans");
        plansResponse.EnsureSuccessStatusCode();
        using var plans = JsonDocument.Parse(await plansResponse.Content.ReadAsStringAsync());
        Assert.AreEqual("pro", plans.RootElement.GetProperty("plans")[0].GetProperty("handle").GetString());

        var subscribeResponse = await client.PostAsJsonAsync(
            "api/subscriptions",
            new { productHandle = "pro" });
        subscribeResponse.EnsureSuccessStatusCode();
        using var created = JsonDocument.Parse(await subscribeResponse.Content.ReadAsStringAsync());
        Assert.AreEqual("active", created.RootElement.GetProperty("subscription").GetProperty("state").GetString());
        Assert.IsNotNull(factory.BillingService.LastUser);
        Assert.AreEqual("demouser@microsoft.com", factory.BillingService.LastUser!.Email);
        Assert.AreEqual("DEMOUSER@MICROSOFT.COM", factory.BillingService.LastUser.Id);

        var mineResponse = await client.GetAsync("api/my-subscriptions");
        mineResponse.EnsureSuccessStatusCode();
        using var mine = JsonDocument.Parse(await mineResponse.Content.ReadAsStringAsync());
        Assert.AreEqual(1, mine.RootElement.GetProperty("subscriptions").GetArrayLength());
    }

    private sealed class SubscriptionApiFactory : WebApplicationFactory<Program>
    {
        public FakeSubscriptionBillingService BillingService { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISubscriptionBillingService>();
                services.AddSingleton<ISubscriptionBillingService>(BillingService);
            });
        }
    }

    private sealed class FakeSubscriptionBillingService : ISubscriptionBillingService
    {
        private static readonly SubscriptionSummary Subscription = new(
            100,
            "pro",
            "Pro",
            "Default",
            29900,
            1,
            "month",
            "active",
            DateTimeOffset.Parse("2030-01-01T00:00:00Z"));

        public BillingUser? LastUser { get; private set; }

        public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlan>>(new[]
            {
                new SubscriptionPlan("pro", "Pro", "", 29900, 1, "month", false)
            });

        public Task<SubscriptionSummary> SubscribeAsync(
            BillingUser user,
            string productHandle,
            CancellationToken cancellationToken = default)
        {
            LastUser = user;
            return Task.FromResult(Subscription);
        }

        public Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsAsync(
            string userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionSummary>>(new[] { Subscription });
    }
}
