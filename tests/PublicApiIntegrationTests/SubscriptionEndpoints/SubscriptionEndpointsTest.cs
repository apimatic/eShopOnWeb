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
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    [TestMethod]
    public async Task AllSubscriptionEndpointsRequireJwtAuthentication()
    {
        using var factory = CreateFactory(new StubSubscriptionBillingService());
        using var client = factory.CreateClient();

        var plans = await client.GetAsync("api/subscription-plans");
        var create = await client.PostAsJsonAsync("api/subscriptions", new CreateSubscriptionRequest("eshop-pro"));
        var subscriptions = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, plans.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, subscriptions.StatusCode);
    }

    [TestMethod]
    public async Task AuthenticatedShopperCanBrowseSubscribeAndReadAccount()
    {
        var billing = new StubSubscriptionBillingService();
        using var factory = CreateFactory(billing);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", ApiTokenHelper.GetNormalUserToken());

        var plans = await client.GetFromJsonAsync<SubscriptionPlanListResponse>("api/subscription-plans");
        var create = await client.PostAsJsonAsync("api/subscriptions", new CreateSubscriptionRequest("eshop-pro"));
        var subscriptions = await client.GetFromJsonAsync<MySubscriptionsResponse>("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode);
        Assert.AreEqual("eshop-pro", plans!.Plans[0].ProductHandle);
        Assert.AreEqual("demouser@microsoft.com", billing.LastShopper!.Email);
        Assert.AreEqual("eshop-pro", subscriptions!.Subscriptions[0].ProductHandle);
        Assert.AreEqual(29900L, subscriptions.Subscriptions[0].PriceInCents);
    }

    private static WebApplicationFactory<Program> CreateFactory(ISubscriptionBillingService billingService) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISubscriptionBillingService>();
                services.AddSingleton(billingService);
            });
        });

    private sealed class StubSubscriptionBillingService : ISubscriptionBillingService
    {
        private readonly SubscriptionDto _subscription = new(
            42, "eshop-pro", "Pro", 29900, "USD", 1, "month", "active",
            new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));

        public ShopperIdentity? LastShopper { get; private set; }

        public Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlanDto>>(new[]
            {
                new SubscriptionPlanDto("eshop-pro", "Pro", "Plan", 29900, 1, "month", false)
            });

        public Task<CreateSubscriptionResult> SubscribeAsync(
            ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken)
        {
            LastShopper = shopper;
            return Task.FromResult(new CreateSubscriptionResult(_subscription, true));
        }

        public Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(
            ShopperIdentity shopper, CancellationToken cancellationToken)
        {
            LastShopper = shopper;
            return Task.FromResult<IReadOnlyList<SubscriptionDto>>(new[] { _subscription });
        }
    }
}
