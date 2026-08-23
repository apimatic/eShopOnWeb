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
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointTests
{
    [TestMethod]
    public async Task PlansRequireJwtAuthentication()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AuthenticatedShopperCanListPlansAndSubscribe()
    {
        var fake = new FakeBillingService();
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var plansResponse = await client.GetAsync("/api/subscription-plans");
        var subscribeResponse = await client.PostAsJsonAsync("/api/subscriptions", new SubscribeRequest { ProductHandle = "eshop-pro" });
        var myResponse = await client.GetAsync("/api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.OK, plansResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, subscribeResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, myResponse.StatusCode);
        Assert.AreEqual("eshop-pro", fake.LastProductHandle);
        Assert.AreEqual("demouser@microsoft.com", fake.LastUser?.Email);
        Assert.AreEqual("Demo", fake.LastUser?.FirstName);
    }

    private static WebApplicationFactory<Program> CreateFactory(ISubscriptionBillingService? service = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                if (service is not null)
                {
                    services.RemoveAll<ISubscriptionBillingService>();
                    services.AddSingleton(service);
                }
            }));

    private sealed class FakeBillingService : ISubscriptionBillingService
    {
        private static readonly SubscriptionDto Subscription = new(
            9,
            "test-reference",
            "eshop-pro",
            "Pro",
            29900,
            "USD",
            "active",
            new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));

        public BillingUser? LastUser { get; private set; }
        public string? LastProductHandle { get; private set; }

        public Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlanDto>>(new[]
            {
                new SubscriptionPlanDto("eshop-pro", "Pro", null, 29900, 1, "month", "USD")
            });

        public Task<SubscribeResult> SubscribeAsync(BillingUser user, string productHandle, CancellationToken cancellationToken)
        {
            LastUser = user;
            LastProductHandle = productHandle;
            return Task.FromResult(new SubscribeResult(Subscription, true));
        }

        public Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionDto>>(new[] { Subscription });
    }
}
