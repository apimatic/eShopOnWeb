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
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionHttpEndpointTests
{
    [TestMethod]
    public async Task SubscriptionPlansRequiresJwt()
    {
        using var factory = CreateFactory(new StubBillingService());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AuthenticatedUserCanListPlansAndSubscribe()
    {
        var billing = new StubBillingService();
        using var factory = CreateFactory(billing);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());

        var plansResponse = await client.GetAsync("/api/subscription-plans");
        var createResponse = await client.PostAsJsonAsync(
            "/api/subscriptions",
            new { productHandle = "eshop-pro" });
        var myResponse = await client.GetAsync("/api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.OK, plansResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, myResponse.StatusCode);
        Assert.AreEqual("eshop-pro", billing.RequestedHandle);
        Assert.AreEqual("demouser@microsoft.com", billing.User?.Email);
        Assert.IsFalse(string.IsNullOrWhiteSpace(billing.User?.Id));
    }

    private static WebApplicationFactory<Program> CreateFactory(ISubscriptionBillingService billingService) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["UseOnlyInMemoryDatabase"] = "true",
                    ["Maxio:ApiKey"] = "not-a-secret",
                    ["Maxio:Subdomain"] = "example",
                    ["Maxio:ProductFamilyHandle"] = "family-under-test"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISubscriptionBillingService>();
                services.AddSingleton(billingService);
            });
        });

    private sealed class StubBillingService : ISubscriptionBillingService
    {
        private readonly SubscriptionDetails _subscription = new(
            8001,
            "eshop-pro",
            "Pro Plan",
            29900,
            "USD",
            "active",
            new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero));

        public SubscriptionUser? User { get; private set; }
        public string? RequestedHandle { get; private set; }

        public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlan>>(new[]
            {
                new SubscriptionPlan("eshop-pro", "Pro Plan", null, 29900, 1, "month", false)
            });

        public Task<SubscribeResult> SubscribeAsync(
            SubscriptionUser user,
            string productHandle,
            CancellationToken cancellationToken = default)
        {
            User = user;
            RequestedHandle = productHandle;
            return Task.FromResult(new SubscribeResult(_subscription, false));
        }

        public Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(
            string userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionDetails>>(new[] { _subscription });
    }
}
