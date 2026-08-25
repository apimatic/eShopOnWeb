using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public sealed class SubscriptionEndpointTests
{
    [TestMethod]
    public async Task AllSubscriptionRoutesRequireBearerAuthentication()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "eshop-pro" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/my-subscriptions")).StatusCode);
    }

    [TestMethod]
    public async Task AuthenticatedRoutesUseTheIdentityFromTheToken()
    {
        var fake = new FakeSubscriptionBillingService();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISubscriptionBillingService>();
                services.AddSingleton<ISubscriptionBillingService>(fake);
            }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var plans = await client.GetAsync("/api/subscription-plans");
        var subscribe = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "eshop-pro" });
        var mine = await client.GetAsync("/api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.OK, plans.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, subscribe.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, mine.StatusCode);
        Assert.AreEqual("test-user:demouser@microsoft.com", fake.LastUser?.Id);
        Assert.AreEqual("demouser@microsoft.com", fake.LastUser?.Email);
        Assert.AreEqual("eshop-pro", fake.LastProductHandle);
    }

    private sealed class FakeSubscriptionBillingService : ISubscriptionBillingService
    {
        private static readonly SubscriptionDto Subscription = new(
            42,
            "test-reference",
            "eshop-pro",
            "Pro",
            29900,
            "active",
            DateTimeOffset.Parse("2026-09-25T00:00:00Z"),
            DateTimeOffset.Parse("2026-09-25T00:00:00Z"));

        public BillingUser? LastUser { get; private set; }
        public string? LastProductHandle { get; private set; }

        public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlanDto>>(
                new[] { new SubscriptionPlanDto("eshop-pro", "Pro", null, 29900, 1, "month", false) });

        public Task<SubscriptionDto> SubscribeAsync(BillingUser user, string productHandle, CancellationToken cancellationToken)
        {
            LastUser = user;
            LastProductHandle = productHandle;
            return Task.FromResult(Subscription);
        }

        public Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(BillingUser user, CancellationToken cancellationToken)
        {
            LastUser = user;
            return Task.FromResult<IReadOnlyList<SubscriptionDto>>(new[] { Subscription });
        }
    }
}
