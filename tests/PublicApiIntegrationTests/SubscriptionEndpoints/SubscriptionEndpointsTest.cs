using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.PublicApi.AuthEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public sealed class SubscriptionEndpointsTest
{
    [TestMethod]
    public async Task AllSubscriptionRoutesRequireAJwt()
    {
        using var factory = CreateFactory(new StubBillingService());
        using var client = factory.CreateClient();

        var plans = await client.GetAsync("/api/subscription-plans");
        var create = await client.PostAsJsonAsync(
            "/api/subscriptions",
            new CreateSubscriptionRequest { ProductHandle = "eshop-pro" });
        var mine = await client.GetAsync("/api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, plans.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, mine.StatusCode);
    }

    [TestMethod]
    public async Task HeroRoutesUseTheAuthenticatedUsersStableIdentity()
    {
        var billing = new StubBillingService();
        using var factory = CreateFactory(billing);
        using var client = factory.CreateClient();
        var auth = await client.PostAsJsonAsync("/api/authenticate", new AuthenticateRequest
        {
            Username = "demouser@microsoft.com",
            Password = AuthorizationConstants.DEFAULT_PASSWORD
        });
        auth.EnsureSuccessStatusCode();
        var authResult = await auth.Content.ReadFromJsonAsync<AuthenticateResponse>();
        Assert.IsNotNull(authResult);
        Assert.IsTrue(authResult.Result);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult.Token);

        var plans = await client.GetFromJsonAsync<SubscriptionPlan[]>("/api/subscription-plans");
        var create = await client.PostAsJsonAsync(
            "/api/subscriptions",
            new CreateSubscriptionRequest { ProductHandle = "eshop-pro" });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<SubscriptionDetails>();
        var mine = await client.GetFromJsonAsync<SubscriptionDetails[]>("/api/my-subscriptions");

        Assert.AreEqual("eshop-pro", plans!.Single().Handle);
        Assert.AreEqual("eshop-pro", created!.ProductHandle);
        Assert.AreEqual("demouser@microsoft.com", billing.Shopper!.Email);
        Assert.IsFalse(string.IsNullOrWhiteSpace(billing.Shopper.UserId));
        Assert.AreEqual(billing.Shopper.UserId, billing.ListedForUserId);
        Assert.AreEqual(created.Id, mine!.Single().Id);
    }

    private static WebApplicationFactory<Program> CreateFactory(StubBillingService billing) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISubscriptionBillingService>();
                services.AddSingleton<ISubscriptionBillingService>(billing);
            }));

    private sealed class StubBillingService : ISubscriptionBillingService
    {
        private readonly SubscriptionDetails _subscription = new(
            42,
            "stable-reference",
            "eshop-pro",
            "Pro Plan",
            29900,
            "active",
            DateTimeOffset.Parse("2030-01-01T00:00:00Z"),
            1,
            "month",
            "USD");

        public SubscriptionShopper? Shopper { get; private set; }

        public string? ListedForUserId { get; private set; }

        public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlan>>(
                new[] { new SubscriptionPlan("eshop-pro", "Pro Plan", null, 29900, 1, "month", false) });

        public Task<SubscriptionDetails> SubscribeAsync(
            SubscriptionShopper shopper,
            string productHandle,
            CancellationToken cancellationToken)
        {
            Shopper = shopper;
            Assert.AreEqual("eshop-pro", productHandle);
            return Task.FromResult(_subscription);
        }

        public Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(
            string userId,
            CancellationToken cancellationToken)
        {
            ListedForUserId = userId;
            return Task.FromResult<IReadOnlyList<SubscriptionDetails>>(new[] { _subscription });
        }
    }
}
