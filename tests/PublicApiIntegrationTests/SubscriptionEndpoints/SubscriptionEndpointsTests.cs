using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Integration tests for the subscription billing endpoints (Maxio
/// Advanced Billing). Tests that hit Maxio run only when the MAXIO_*
/// environment variables are configured; otherwise they are skipped.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTests
{
    private static WebApplicationFactory<Program>? _maxioFactory;

    private static HttpClient GetClient()
    {
        return ProgramTest.NewClient;
    }

    private static bool TryGetMaxioConfig(out Dictionary<string, string?> config)
    {
        config = new Dictionary<string, string?>();
        var apiKey = Environment.GetEnvironmentVariable("MAXIO_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            return false;
        }

        config["Maxio:ApiKey"] = apiKey;
        config["Maxio:Subdomain"] = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN");
        config["Maxio:ProductFamilyHandle"] = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY");
        return true;
    }

    private static HttpClient GetMaxioClient()
    {
        if (_maxioFactory == null)
        {
            TryGetMaxioConfig(out var config);
            _maxioFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(config));
            });
        }

        return _maxioFactory.CreateClient();
    }

    private static HttpClient GetAuthenticatedClient()
    {
        var client = GetMaxioClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutToken()
    {
        var response = await GetClient().GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedForCreateWithoutToken()
    {
        var response = await GetClient().PostAsync("api/subscriptions",
            new StringContent("{\"planHandle\":\"any\"}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsPlans()
    {
        if (!TryGetMaxioConfig(out _))
        {
            Assert.Inconclusive("MAXIO credentials are not configured.");
        }

        var client = GetAuthenticatedClient();
        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<SubscriptionPlanListResponse>();

        Assert.IsNotNull(model);
        Assert.IsTrue(model!.Plans.Any(), "The configured product family should contain at least one plan.");
        Assert.IsTrue(model.Plans.All(p => !string.IsNullOrWhiteSpace(p.Handle)));
        Assert.IsTrue(model.Plans.All(p => p.PriceInCents >= 0));
    }

    [TestMethod]
    public async Task SubscribeIsIdempotent()
    {
        if (!TryGetMaxioConfig(out _))
        {
            Assert.Inconclusive("MAXIO credentials are not configured.");
        }

        var client = GetAuthenticatedClient();
        var plansResponse = await client.GetAsync("api/subscription-plans");
        plansResponse.EnsureSuccessStatusCode();
        var plans = (await plansResponse.Content.ReadAsStringAsync()).FromJson<SubscriptionPlanListResponse>();
        Assert.IsNotNull(plans);
        var plan = plans!.Plans.First();

        var request = new StringContent($"{{\"planHandle\":\"{plan.Handle}\"}}", Encoding.UTF8, "application/json");
        var first = await client.PostAsync("api/subscriptions", request);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        var firstModel = (await first.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsNotNull(firstModel);
        Assert.IsFalse(firstModel!.AlreadySubscribed);
        Assert.AreEqual(plan.Handle, firstModel.Subscription.PlanHandle);
        Assert.AreEqual(plan.PriceInCents, firstModel.Subscription.PriceInCents);
        Assert.AreEqual("active", firstModel.Subscription.State);
        Assert.IsTrue(firstModel.Subscription.NextBillingAt.HasValue);

        // A second identical request must not create a second subscription.
        request = new StringContent($"{{\"planHandle\":\"{plan.Handle}\"}}", Encoding.UTF8, "application/json");
        var second = await client.PostAsync("api/subscriptions", request);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var secondModel = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsNotNull(secondModel);
        Assert.IsTrue(secondModel!.AlreadySubscribed);
        Assert.AreEqual(firstModel.Subscription.SubscriptionId, secondModel.Subscription.SubscriptionId);

        // my-subscriptions reflects the subscription held in the billing system.
        var mine = await client.GetAsync("api/my-subscriptions");
        mine.EnsureSuccessStatusCode();
        var mineModel = (await mine.Content.ReadAsStringAsync()).FromJson<MySubscriptionsResponse>();
        Assert.IsNotNull(mineModel);
        Assert.IsTrue(mineModel!.Subscriptions.Any(s => s.SubscriptionId == firstModel.Subscription.SubscriptionId));

        await CancelSubscriptionAsync(firstModel.Subscription.SubscriptionId);
    }

    [TestMethod]
    public async Task ReturnsNotFoundForUnknownPlan()
    {
        if (!TryGetMaxioConfig(out _))
        {
            Assert.Inconclusive("MAXIO credentials are not configured.");
        }

        var client = GetAuthenticatedClient();
        var response = await client.PostAsync("api/subscriptions",
            new StringContent("{\"planHandle\":\"this-plan-does-not-exist\"}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsBadRequestWhenNoPlanSupplied()
    {
        if (!TryGetMaxioConfig(out _))
        {
            Assert.Inconclusive("MAXIO credentials are not configured.");
        }

        var client = GetAuthenticatedClient();
        var response = await client.PostAsync("api/subscriptions",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task CancelSubscriptionAsync(int subscriptionId)
    {
        var apiKey = Environment.GetEnvironmentVariable("MAXIO_API_KEY");
        var subdomain = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN");
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(subdomain))
        {
            return;
        }

        using var http = new HttpClient();
        http.BaseAddress = new Uri($"https://{subdomain}.chargify.com/");
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x")));
        await http.DeleteAsync($"subscriptions/{subscriptionId}.json");
    }
}