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
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Billing;
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
        using var application = CreateApplication(new StubSubscriptionService());
        using var client = application.CreateClient();

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionPlansReturnsConfiguredPlansForAuthenticatedUser()
    {
        using var application = CreateApplication(new StubSubscriptionService());
        using var client = AuthenticatedClient(application);

        var response = await client.GetAsync("api/subscription-plans");
        var plans = await response.Content.ReadFromJsonAsync<List<SubscriptionPlanDto>>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(plans);
        Assert.AreEqual("eshop-pro", plans.Single().ProductHandle);
        Assert.AreEqual(29900, plans.Single().PriceInCents);
    }

    [TestMethod]
    public async Task SubscribeUsesIdentityFromBearerToken()
    {
        var service = new StubSubscriptionService();
        using var application = CreateApplication(service);
        using var client = AuthenticatedClient(application);

        var response = await client.PostAsJsonAsync("api/subscriptions", new CreateSubscriptionRequest
        {
            ProductHandle = "eshop-pro"
        });
        var subscription = await response.Content.ReadFromJsonAsync<SubscriptionDto>();

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual("test-user-id", service.UserId);
        Assert.AreEqual("demouser@microsoft.com", service.Email);
        Assert.AreEqual("eshop-pro", subscription!.ProductHandle);
        Assert.AreEqual("active", subscription.State);
    }

    private static WebApplicationFactory<Program> CreateApplication(ISubscriptionService service) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISubscriptionService>();
                services.AddSingleton(service);
            }));

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> application)
    {
        var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private sealed class StubSubscriptionService : ISubscriptionService
    {
        public string? UserId { get; private set; }
        public string? Email { get; private set; }

        public Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BillingPlan>>(new[]
            {
                new BillingPlan(2, "eshop-pro", "Pro", "Pro plan", 29900, 1, "month", false)
            });

        public Task<SubscribeResult> SubscribeAsync(string userId, string email, string productHandle, CancellationToken cancellationToken = default)
        {
            UserId = userId;
            Email = email;
            var subscription = new BillingSubscription(
                42,
                "reference",
                productHandle,
                "Pro",
                29900,
                1,
                "month",
                "active",
                DateTimeOffset.UtcNow.AddMonths(1),
                7,
                "family-under-test");
            return Task.FromResult(new SubscribeResult(subscription, true));
        }

        public Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BillingSubscription>>(Array.Empty<BillingSubscription>());
    }
}
