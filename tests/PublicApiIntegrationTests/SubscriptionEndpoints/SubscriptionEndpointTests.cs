using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public sealed class SubscriptionEndpointTests
{
    [TestMethod]
    public async Task PlansRequireJwtAuthentication()
    {
        var (client, _) = CreateClient();
        var response = await client.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PlansReturnGatewayProjectionForAuthenticatedUser()
    {
        var (client, billing) = CreateClient();
        Authorize(client);

        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();
        var plans = await response.Content.ReadFromJsonAsync<List<SubscriptionPlan>>();

        Assert.IsNotNull(plans);
        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual(42, plans[0].ProductId);
        Assert.AreEqual(1, billing.ListPlansCalls);
    }

    [TestMethod]
    public async Task SubscribeUsesIdentityFromJwt()
    {
        var (client, billing) = CreateClient();
        Authorize(client);

        var response = await client.PostAsJsonAsync("api/subscriptions", new { productId = 42 });
        response.EnsureSuccessStatusCode();

        Assert.IsNotNull(billing.LastIdentity);
        Assert.AreEqual("test-user-id", billing.LastIdentity.UserId);
        Assert.AreEqual("demouser@microsoft.com", billing.LastIdentity.Email);
        Assert.AreEqual(42, billing.LastProductId);
    }

    [TestMethod]
    public async Task MySubscriptionsUseIdentityFromJwt()
    {
        var (client, billing) = CreateClient();
        Authorize(client);

        var response = await client.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();

        Assert.IsNotNull(billing.LastIdentity);
        Assert.AreEqual("test-user-id", billing.LastIdentity.UserId);
    }

    private static (HttpClient Client, FakeSubscriptionBillingService Billing) CreateClient()
    {
        var billing = new FakeSubscriptionBillingService();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISubscriptionBillingService>();
                services.AddSingleton<ISubscriptionBillingService>(billing);
            });
        });

        return (factory.CreateClient(), billing);
    }

    private static void Authorize(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
    }

    private sealed class FakeSubscriptionBillingService : ISubscriptionBillingService
    {
        private static readonly SubscriptionDetails Subscription =
            new(7, "reference", "Pro", "pro", 29900, "active", DateTimeOffset.Parse("2030-01-01T00:00:00Z"));

        public int ListPlansCalls { get; private set; }
        public BillingIdentity? LastIdentity { get; private set; }
        public int LastProductId { get; private set; }

        public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
        {
            ListPlansCalls++;
            IReadOnlyList<SubscriptionPlan> plans =
            [new(42, "pro", "Pro", "Pro plan", 29900, 1, "month", 99, "default", "Default")];
            return Task.FromResult(plans);
        }

        public Task<SubscriptionDetails> SubscribeAsync(
            BillingIdentity identity,
            int productId,
            CancellationToken cancellationToken)
        {
            LastIdentity = identity;
            LastProductId = productId;
            return Task.FromResult(Subscription);
        }

        public Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(
            BillingIdentity identity,
            CancellationToken cancellationToken)
        {
            LastIdentity = identity;
            IReadOnlyList<SubscriptionDetails> subscriptions = [Subscription];
            return Task.FromResult(subscriptions);
        }
    }
}
