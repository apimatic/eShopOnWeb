using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionsEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static WebApplicationFactory<Program> BuildFactory(FakeMaxioApiClient fake)
    {
        var factory = new WebApplicationFactory<Program>();
        return factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["UseOnlyInMemoryDatabase"] = "true",
                    ["Maxio:ApiKey"] = "test-key",
                    ["Maxio:Subdomain"] = "test",
                    ["Maxio:ProductFamilyHandle"] = "eshop-subscribe"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMaxioApiClient>();
                services.AddSingleton<IMaxioApiClient>(fake);
            });
        });
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body.ToJson(), System.Text.Encoding.UTF8, "application/json")
        };
        if (token != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }

    [TestMethod]
    public async Task RequiresAuthentication()
    {
        using var factory = BuildFactory(new FakeMaxioApiClient(FakeMaxioCatalog.Plans));
        var client = factory.CreateClient();

        var plansResponse = await GetAsync(client, "api/subscription-plans");
        var subscribeResponse = await PostAsync(client, "api/subscriptions", new { planHandle = "eshop-pro" });
        var myResponse = await GetAsync(client, "api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, plansResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, subscribeResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, myResponse.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsAvailablePlansForAuthenticatedUser()
    {
        using var factory = BuildFactory(new FakeMaxioApiClient(FakeMaxioCatalog.Plans));
        var client = factory.CreateClient();
        var token = ApiTokenHelper.GetNormalUserToken();

        var response = await GetAsync(client, "api/subscription-plans", token);
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();
        Assert.IsNotNull(model);
        Assert.AreEqual(2, model!.SubscriptionPlans.Count);

        // Sorted by price ascending: Basic ($29) before Pro ($299).
        Assert.AreEqual("basic-plan", model.SubscriptionPlans[0].Handle);
        Assert.AreEqual("eshop-pro", model.SubscriptionPlans[1].Handle);
        Assert.AreEqual(2900, model.SubscriptionPlans[0].PriceInCents);
        Assert.AreEqual(29m, model.SubscriptionPlans[0].Price);
        Assert.AreEqual("month", model.SubscriptionPlans[0].IntervalUnit);
    }

    [TestMethod]
    public async Task CreatesSubscriptionWhenNoneExists()
    {
        using var factory = BuildFactory(new FakeMaxioApiClient(FakeMaxioCatalog.Plans));
        var client = factory.CreateClient();
        var token = ApiTokenHelper.GetNormalUserToken();

        var response = await PostAsync(client, "api/subscriptions", new { planHandle = "eshop-pro" }, token);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var model = (await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsNotNull(model);
        Assert.AreEqual("eshop-pro", model!.Subscription.PlanHandle);
        Assert.AreEqual("Pro Plan", model.Subscription.PlanName);
        Assert.AreEqual(29900, model.Subscription.PriceInCents);
        Assert.AreEqual(299m, model.Subscription.Price);
        Assert.AreEqual("active", model.Subscription.State);
        Assert.IsNotNull(model.Subscription.CurrentPeriodEndsAt);
        Assert.IsNotNull(model.Subscription.SubscriptionId);
    }

    [TestMethod]
    public async Task SubscribeIsIdempotent()
    {
        using var factory = BuildFactory(new FakeMaxioApiClient(FakeMaxioCatalog.Plans));
        var client = factory.CreateClient();
        var token = ApiTokenHelper.GetNormalUserToken();

        var firstResponse = await PostAsync(client, "api/subscriptions", new { planHandle = "eshop-pro" }, token);
        var secondResponse = await PostAsync(client, "api/subscriptions", new { planHandle = "eshop-pro" }, token);

        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, secondResponse.StatusCode);

        var first = (await firstResponse.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        var second = (await secondResponse.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();

        Assert.AreEqual(first!.Subscription.SubscriptionId, second!.Subscription.SubscriptionId);
    }

    [TestMethod]
    public async Task SubscribeToUnknownPlanReturnsNotFound()
    {
        using var factory = BuildFactory(new FakeMaxioApiClient(FakeMaxioCatalog.Plans));
        var client = factory.CreateClient();
        var token = ApiTokenHelper.GetNormalUserToken();

        var response = await PostAsync(client, "api/subscriptions", new { planHandle = "does-not-exist" }, token);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsReturnsSubscriptionsForCurrentUserOnly()
    {
        using var factory = BuildFactory(new FakeMaxioApiClient(FakeMaxioCatalog.Plans));
        var client = factory.CreateClient();
        var token = ApiTokenHelper.GetNormalUserToken();

        await PostAsync(client, "api/subscriptions", new { planHandle = "basic-plan" }, token);

        var response = await GetAsync(client, "api/my-subscriptions", token);
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<MySubscriptionsResponse>();
        Assert.IsNotNull(model);
        Assert.AreEqual(1, model!.Subscriptions.Count);
        Assert.AreEqual("basic-plan", model.Subscriptions[0].PlanHandle);
        Assert.AreEqual("active", model.Subscriptions[0].State);
    }
}
