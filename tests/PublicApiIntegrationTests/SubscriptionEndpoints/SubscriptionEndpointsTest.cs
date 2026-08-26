using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static readonly WebApplicationFactory<Program> _application = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Never call the real Maxio API from tests.
                services.RemoveAll<ISubscriptionBillingService>();
                services.AddSingleton<ISubscriptionBillingService>(new FakeSubscriptionBillingService());
            });
        });

    [TestMethod]
    public async Task SubscriptionEndpointsRequireAuthentication()
    {
        var client = _application.CreateClient();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("api/subscriptions", new CreateSubscriptionRequest { ProductHandle = "eshop-pro" })).StatusCode);
    }

    [TestMethod]
    public async Task ListSubscriptionPlansReturnsPlans()
    {
        var client = NewAuthorizedClient();

        var response = await client.GetFromJsonAsync<ListSubscriptionPlansResponse>("api/subscription-plans");

        Assert.IsNotNull(response);
        Assert.AreEqual(2, response.Plans.Count);
        Assert.IsTrue(response.Plans.Any(p => p.Handle == "eshop-pro" && p.PriceInCents == 29900));
    }

    [TestMethod]
    public async Task SubscribeReturnsSubscriptionConfirmation()
    {
        var client = NewAuthorizedClient();

        var response = await client.PostAsJsonAsync("api/subscriptions", new CreateSubscriptionRequest { ProductHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();
        Assert.IsNotNull(content);
        Assert.IsFalse(content.AlreadyExisted);
        Assert.IsNotNull(content.Subscription);
        Assert.AreEqual("active", content.Subscription.State);
        Assert.AreEqual("eshop-pro", content.Subscription.PlanHandle);
        Assert.AreEqual(29900, content.Subscription.PriceInCents);
        Assert.IsNotNull(content.Subscription.NextBillingDate);
    }

    [TestMethod]
    public async Task SubscribeWithoutPlanHandleReturnsBadRequest()
    {
        var client = NewAuthorizedClient();

        var response = await client.PostAsJsonAsync("api/subscriptions", new CreateSubscriptionRequest());

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptionsReturnsSubscriptions()
    {
        var client = NewAuthorizedClient();

        var response = await client.GetFromJsonAsync<ListMySubscriptionsResponse>("api/my-subscriptions");

        Assert.IsNotNull(response);
        Assert.AreEqual(1, response.Subscriptions.Count);
        Assert.AreEqual("eshop-pro", response.Subscriptions[0].PlanHandle);
    }

    private static HttpClient NewAuthorizedClient()
    {
        var client = _application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private class FakeSubscriptionBillingService : ISubscriptionBillingService
    {
        public Task<IReadOnlyList<SubscriptionPlan>> ListSubscriptionPlansAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SubscriptionPlan> plans = new List<SubscriptionPlan>
            {
                new() { ProductId = 1, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" },
                new() { ProductId = 2, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
            };
            return Task.FromResult(plans);
        }

        public Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
        {
            var result = new SubscribeResult(new CustomerSubscription
            {
                SubscriptionId = 42,
                State = "active",
                PlanHandle = request.PlanHandle,
                PlanName = "Pro Plan",
                PriceInCents = 29900,
                Interval = 1,
                IntervalUnit = "month",
                NextBillingDate = System.DateTimeOffset.UtcNow.AddMonths(1),
                CreatedAt = System.DateTimeOffset.UtcNow
            }, alreadyExisted: false);
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<CustomerSubscription> subscriptions = new List<CustomerSubscription>
            {
                new() { SubscriptionId = 42, State = "active", PlanHandle = "eshop-pro", PlanName = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
            };
            return Task.FromResult(subscriptions);
        }
    }
}
