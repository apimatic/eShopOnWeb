using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Exercises the Maxio-backed subscription endpoints end to end. These tests call the
/// real Maxio sandbox and therefore only run when the Maxio integration is configured
/// (Maxio:ApiKey/Maxio:Subdomain/Maxio:ProductFamilyHandle present in the PublicApi user
/// secrets). In CI, where no Maxio credentials exist, the tests no-op so the build stays
/// green without a live billing sandbox.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTests
{
    private const string NotConfiguredMarker = "Maxio is not configured";

    [TestMethod]
    public async Task RequiresAuthentication()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionPlans_ReturnsConfiguredCatalog_WhenMaxioConfigured()
    {
        var client = GetAuthenticatedClient();
        var response = await client.GetAsync("api/subscription-plans");
        if (await ShouldSkip(response))
        {
            return;
        }

        var json = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode,
            $"GET api/subscription-plans failed with {(int)response.StatusCode}: {json}");

        var model = json.FromJson<ListSubscriptionPlansResponse>();
        Assert.IsNotNull(model);
        Assert.IsTrue(model!.Plans.Count > 0, "Expected at least one available plan.");
        Assert.IsTrue(model.Plans.All(p =>
                !string.IsNullOrWhiteSpace(p.Handle) &&
                !string.IsNullOrWhiteSpace(p.Name) &&
                p.PriceInCents >= 0),
            "Every plan should expose a handle, name and non-negative price.");

        var configuredFamily = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY");
        if (!string.IsNullOrWhiteSpace(configuredFamily))
        {
            Assert.IsTrue(model.Plans.All(p =>
                    string.Equals(p.ProductFamilyHandle, configuredFamily, StringComparison.OrdinalIgnoreCase)),
                "All returned plans should belong to the configured product family.");
        }
    }

    [TestMethod]
    public async Task Subscribe_IsIdempotent_WhenMaxioConfigured()
    {
        var client = GetAuthenticatedClient();

        var plansResponse = await client.GetAsync("api/subscription-plans");
        if (await ShouldSkip(plansResponse))
        {
            return;
        }

        var plansJson = await plansResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(plansResponse.IsSuccessStatusCode,
            $"GET api/subscription-plans failed with {(int)plansResponse.StatusCode}: {plansJson}");
        var plans = plansJson.FromJson<ListSubscriptionPlansResponse>();
        if (plans is null || plans.Plans.Count == 0)
        {
            return;
        }

        var planHandle = plans.Plans.First().Handle;

        var first = await PostSubscriptionAsync(client, planHandle);
        var firstJson = await first.Content.ReadAsStringAsync();
        Assert.IsTrue(first.IsSuccessStatusCode,
            $"First subscribe failed with {(int)first.StatusCode}: {firstJson}");
        var firstModel = firstJson.FromJson<CreateSubscriptionResponse>();
        Assert.IsNotNull(firstModel);
        Assert.IsTrue(firstModel!.Subscription.SubscriptionId > 0);
        Assert.IsFalse(string.IsNullOrWhiteSpace(firstModel.Subscription.State));
        Assert.AreEqual(planHandle, firstModel.Subscription.PlanHandle);

        var second = await PostSubscriptionAsync(client, planHandle);
        var secondJson = await second.Content.ReadAsStringAsync();
        Assert.IsTrue(second.IsSuccessStatusCode,
            $"Second (duplicate) subscribe failed with {(int)second.StatusCode}: {secondJson}");
        var secondModel = secondJson.FromJson<CreateSubscriptionResponse>();

        Assert.IsNotNull(secondModel);
        Assert.AreEqual(
            firstModel.Subscription.SubscriptionId,
            secondModel!.Subscription.SubscriptionId,
            "Subscribing twice to the same plan must not create a second subscription.");

        var mineResponse = await client.GetAsync("api/my-subscriptions");
        var mineJson = await mineResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(mineResponse.IsSuccessStatusCode,
            $"GET api/my-subscriptions failed with {(int)mineResponse.StatusCode}: {mineJson}");
        var mine = mineJson.FromJson<ListSubscriptionsResponse>();

        Assert.IsNotNull(mine);
        Assert.IsTrue(mine!.Subscriptions.Any(s => s.SubscriptionId == firstModel.Subscription.SubscriptionId),
            "The created subscription should be visible in my-subscriptions.");
    }

    private static HttpClient GetAuthenticatedClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static async Task<HttpResponseMessage> PostSubscriptionAsync(HttpClient client, string planHandle)
    {
        var request = new CreateSubscriptionRequest { PlanHandle = planHandle };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        return await client.PostAsync("api/subscriptions", content);
    }

    private static async Task<bool> ShouldSkip(HttpResponseMessage response)
    {
        if ((int)response.StatusCode != 500)
        {
            return false;
        }

        var body = await response.Content.ReadAsStringAsync();
        return body.Contains(NotConfiguredMarker, StringComparison.OrdinalIgnoreCase);
    }
}
