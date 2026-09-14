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
    public async Task SubscriptionEndpointsRequireBearerToken()
    {
        await using var factory = CreateFactory(new FakeBillingService());
        using var client = factory.CreateClient();

        var plans = await client.GetAsync("/api/subscription-plans");
        var subscriptions = await client.GetAsync("/api/my-subscriptions");
        var create = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "pro" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, plans.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, subscriptions.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, create.StatusCode);
    }

    [TestMethod]
    public async Task AuthenticatedShopperCanBrowseSubscribeAndReadAccount()
    {
        var billing = new FakeBillingService();
        await using var factory = CreateFactory(billing);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());

        var plans = await client.GetFromJsonAsync<SubscriptionPlanDto[]>("/api/subscription-plans");
        var createResponse = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "pro" });
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>();
        var subscriptions = await client.GetFromJsonAsync<SubscriptionDto[]>("/api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.AreEqual("pro", AssertSingle(plans).Handle);
        Assert.AreEqual("active", created!.State);
        Assert.AreEqual(created.Id, AssertSingle(subscriptions).Id);
        Assert.AreEqual("demouser@microsoft.com", billing.LastUser!.UserId);
        Assert.AreEqual("demouser@microsoft.com", billing.LastUser.Email);
    }

    private static WebApplicationFactory<Program> CreateFactory(ISubscriptionBillingService billingService)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISubscriptionBillingService>();
                services.AddSingleton(billingService);
            });
        });
    }

    private static T AssertSingle<T>(IEnumerable<T>? values)
    {
        Assert.IsNotNull(values);
        var array = values.ToArray();
        Assert.AreEqual(1, array.Length);
        return array[0];
    }

    private sealed class FakeBillingService : ISubscriptionBillingService
    {
        private readonly SubscriptionDetails _subscription = new(
            42,
            "pro",
            "Pro",
            29900,
            "USD",
            "active",
            new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero));

        public SubscriptionUser? LastUser { get; private set; }

        public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SubscriptionPlan> plans = new[]
            {
                new SubscriptionPlan("pro", "Pro", "", 29900, 1, "month")
            };
            return Task.FromResult(plans);
        }

        public Task<SubscriptionDetails> SubscribeAsync(
            SubscriptionUser user,
            string productHandle,
            CancellationToken cancellationToken = default)
        {
            LastUser = user;
            return Task.FromResult(_subscription);
        }

        public Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(
            SubscriptionUser user,
            CancellationToken cancellationToken = default)
        {
            LastUser = user;
            IReadOnlyList<SubscriptionDetails> subscriptions = new[] { _subscription };
            return Task.FromResult(subscriptions);
        }
    }
}
