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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private sealed class FakeSubscriptionService : ISubscriptionService
    {
        public List<(Subscriber Subscriber, string Plan)> Subscribed { get; } = new();

        public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlan>>(new[] { new SubscriptionPlan("eshop-pro", "Pro Plan", null, 29900, 1, "month") });

        public Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default)
        {
            if (planHandle == "missing") throw new SubscriptionPlanNotFoundException(planHandle);
            if (planHandle == "down") throw new BillingUnavailableException("secret internal detail");
            var created = Subscribed.Count == 0;
            Subscribed.Add((subscriber, planHandle));
            return Task.FromResult(new SubscribeResult(
                new UserSubscription("1", planHandle, "Pro Plan", 29900, 1, "month", "active", DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow),
                created));
        }

        public Task<IReadOnlyList<UserSubscription>> ListSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserSubscription>>(Array.Empty<UserSubscription>());
    }

    private static (HttpClient Client, FakeSubscriptionService Service) NewClient(bool authenticated)
    {
        var service = new FakeSubscriptionService();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddScoped<ISubscriptionService>(_ => service)));
        var client = factory.CreateClient();
        if (authenticated)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return (client, service);
    }

    [TestMethod]
    public async Task RequiresAuthentication()
    {
        var (client, _) = NewClient(authenticated: false);

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" })).StatusCode);
    }

    [TestMethod]
    public async Task ListsPlans()
    {
        var (client, _) = NewClient(authenticated: true);

        var response = await client.GetFromJsonAsync<ListSubscriptionPlansResponse>("api/subscription-plans");

        Assert.AreEqual(1, response!.SubscriptionPlans.Count);
        Assert.AreEqual(299m, response.SubscriptionPlans[0].Price);
    }

    [TestMethod]
    public async Task SubscribeReturnsCreatedThenOkOnReplay()
    {
        var (client, service) = NewClient(authenticated: true);

        var first = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });
        var second = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        Assert.AreEqual("demouser@microsoft.com", service.Subscribed[0].Subscriber.Email);
    }

    [TestMethod]
    public async Task SubscribeRejectsMissingPlanHandle()
    {
        var (client, _) = NewClient(authenticated: true);

        var response = await client.PostAsJsonAsync("api/subscriptions", new { });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UnknownPlanIs404()
    {
        var (client, _) = NewClient(authenticated: true);

        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "missing" });

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task BillingOutageIs503WithoutLeakingDetail()
    {
        var (client, _) = NewClient(authenticated: true);

        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "down" });

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        StringAssert.DoesNotMatch(await response.Content.ReadAsStringAsync(), new System.Text.RegularExpressions.Regex("secret"));
    }
}
