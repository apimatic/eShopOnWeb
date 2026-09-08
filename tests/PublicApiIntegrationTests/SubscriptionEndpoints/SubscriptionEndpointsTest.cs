using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static WebApplicationFactory<Program> CreateAppWithFakeBilling(out FakeMaxioBillingClient fake, out HttpClient client)
    {
        var fakeClient = new FakeMaxioBillingClient();
        var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IMaxioBillingClient));
                    if (descriptor is not null)
                    {
                        services.Remove(descriptor);
                    }
                    services.AddScoped<IMaxioBillingClient>(_ => fakeClient);
                });
            });

        fake = fakeClient;

        client = application.CreateClient();
        return application;
    }

    private static void Authenticate(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
    }

    [TestMethod]
    public async Task SubscriptionPlans_RequiresAuthentication()
    {
        var application = CreateAppWithFakeBilling(out _, out var client);

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        application.Dispose();
    }

    [TestMethod]
    public async Task SubscriptionPlans_ReturnsPlansOfConfiguredFamily()
    {
        var application = CreateAppWithFakeBilling(out _, out var client);
        Authenticate(client);

        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var model = await ReadJsonAsync<ListSubscriptionPlansResponse>(response);

        CollectionAssert.Contains(model.SubscriptionPlans.Select(p => p.Handle).ToList(), "eshop-pro");
        CollectionAssert.Contains(model.SubscriptionPlans.Select(p => p.Handle).ToList(), "basic-plan");
        application.Dispose();
    }

    [TestMethod]
    public async Task Subscribe_EndToEnd_ConfirmsPlanPriceStateAndNextBillingDate()
    {
        var application = CreateAppWithFakeBilling(out var fake, out var client);
        Authenticate(client);

        var response = await PostSubscribeAsync(client, new SubscribeRequest { PlanHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var model = await ReadJsonAsync<SubscribeResponse>(response);
        Assert.AreEqual("eshop-pro", model.Subscription.PlanHandle);
        Assert.AreEqual("Pro Plan", model.Subscription.PlanName);
        Assert.AreEqual(29900, model.Subscription.PriceInCents);
        Assert.AreEqual("active", model.Subscription.State);
        Assert.IsTrue(model.Subscription.NextBillingDate > DateTimeOffset.UtcNow);
        Assert.IsFalse(model.AlreadySubscribed);
        Assert.AreEqual(1, fake.CreateCustomerCalls);
        Assert.AreEqual(1, fake.CreateSubscriptionCalls);
        application.Dispose();
    }

    [TestMethod]
    public async Task Subscribe_TwiceInSuccession_CreatesExactlyOneSubscription()
    {
        var application = CreateAppWithFakeBilling(out var fake, out var client);
        Authenticate(client);

        var first = await PostSubscribeAsync(client, new SubscribeRequest { PlanHandle = "eshop-pro" });
        var second = await PostSubscribeAsync(client, new SubscribeRequest { PlanHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);

        var secondModel = await ReadJsonAsync<SubscribeResponse>(second);
        Assert.IsTrue(secondModel.AlreadySubscribed);
        Assert.AreEqual(1, fake.CreateSubscriptionCalls);
        Assert.AreEqual(1, fake.CreateCustomerCalls);
        Assert.AreEqual(1, fake.Subscriptions.Count);
        application.Dispose();
    }

    [TestMethod]
    public async Task Subscribe_DuplicatePreventionConflict_RecoversTheWinningSubscription()
    {
        var application = CreateAppWithFakeBilling(out var fake, out var client);
        Authenticate(client);
        fake.NextCreateSubscriptionConflict = true;

        var response = await PostSubscribeAsync(client, new SubscribeRequest { PlanHandle = "basic-plan" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var model = await ReadJsonAsync<SubscribeResponse>(response);
        Assert.IsTrue(model.AlreadySubscribed);
        Assert.AreEqual("basic-plan", model.Subscription.PlanHandle);
        Assert.AreEqual(1, fake.Subscriptions.Count);
        application.Dispose();
    }

    [TestMethod]
    public async Task Subscribe_IdempotencyKeyIsUsedAsUniquenessScope()
    {
        var application = CreateAppWithFakeBilling(out var fake, out var client);
        Authenticate(client);

        var first = await PostSubscribeAsync(client, new SubscribeRequest { PlanHandle = "eshop-pro", IdempotencyKey = "key-123" });
        first.EnsureSuccessStatusCode();

        Assert.IsTrue(fake.UsedUniquenessTokens.Single().Contains("key-123"));
        application.Dispose();
    }

    [TestMethod]
    public async Task Subscribe_UnknownPlan_IsRejected()
    {
        var application = CreateAppWithFakeBilling(out var fake, out var client);
        Authenticate(client);

        var response = await PostSubscribeAsync(client, new SubscribeRequest { PlanHandle = "not-a-plan" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, fake.CreateSubscriptionCalls);
        application.Dispose();
    }

    [TestMethod]
    public async Task MySubscriptions_ReflectsEnrollment()
    {
        var application = CreateAppWithFakeBilling(out _, out var client);
        Authenticate(client);

        var subscribe = await PostSubscribeAsync(client, new SubscribeRequest { PlanHandle = "eshop-pro" });
        Assert.IsTrue(subscribe.IsSuccessStatusCode,
            $"subscribe failed: {(int)subscribe.StatusCode} {await subscribe.Content.ReadAsStringAsync()}");

        var response = await client.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();

        var model = await ReadJsonAsync<ListMySubscriptionsResponse>(response);
        Assert.AreEqual(1, model.Subscriptions.Count);
        Assert.AreEqual("Pro Plan", model.Subscriptions[0].PlanName);
        Assert.AreEqual("active", model.Subscriptions[0].State);
        application.Dispose();
    }

    private static async Task<HttpResponseMessage> PostSubscribeAsync(HttpClient client, SubscribeRequest request)
    {
        var json = JsonSerializer.Serialize(request);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync("api/subscriptions", content);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
        => (await JsonSerializer.DeserializeAsync<T>(
            await response.Content.ReadAsStreamAsync(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
}
