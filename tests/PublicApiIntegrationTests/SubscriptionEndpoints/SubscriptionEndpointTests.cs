using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointTests
{
    private static readonly BillingSubscription Subscription = new(
        123, "owned-reference", "eshop-pro", "Pro Plan", 29900, "USD", "active",
        DateTimeOffset.Parse("2026-09-27T00:00:00Z"), null);

    [TestMethod]
    public async Task RoutesRequireJwtAuthentication()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var plans = await client.GetAsync("/api/subscription-plans");
        var create = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "eshop-pro" });
        var mine = await client.GetAsync("/api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, plans.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, mine.StatusCode);
    }

    [TestMethod]
    public async Task AuthenticatedHeroFlowReturnsCatalogCreateAndAccountState()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var plans = await client.GetAsync("/api/subscription-plans");
        var create = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "eshop-pro" });
        var mine = await client.GetAsync("/api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.OK, plans.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, mine.StatusCode);
        StringAssert.Contains(await plans.Content.ReadAsStringAsync(), "eshop-pro");
        StringAssert.Contains(await create.Content.ReadAsStringAsync(), "29900");
        StringAssert.Contains(await mine.Content.ReadAsStringAsync(), "active");
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISubscriptionService>();
                services.AddSingleton<ISubscriptionService>(new StubSubscriptionService());
            }));

    private sealed class StubSubscriptionService : ISubscriptionService
    {
        public Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BillingPlan>>(new[]
            {
                new BillingPlan("eshop-pro", "Pro Plan", "Pro", 29900, "USD", 1, "month"),
                new BillingPlan("basic-plan", "Basic Plan", "Basic", 2900, "USD", 1, "month")
            });

        public Task<SubscriptionResult> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken) =>
            Task.FromResult(new SubscriptionResult(Subscription, true));

        public Task<IReadOnlyList<BillingSubscription>> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BillingSubscription>>(new[] { Subscription });
    }
}
