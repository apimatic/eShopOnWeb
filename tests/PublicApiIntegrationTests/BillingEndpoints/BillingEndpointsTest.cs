using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// Runs against the Maxio sandbox (credentials from the MAXIO_* environment variables): nothing is stubbed.
/// </summary>
[TestClass]
public class BillingEndpointsTest
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static HttpClient ClientFor(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, response.StatusCode, body);
        return string.IsNullOrEmpty(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<(int Id, string Handle)> CreatePlanAsync(HttpClient admin, string name, decimal price, int brandId)
    {
        var response = await admin.PostAsJsonAsync("api/admin/plans", new { name, price, brandId });
        var plan = await ReadAsync(response, HttpStatusCode.Created);
        return (plan.GetProperty("id").GetInt32(), plan.GetProperty("handle").GetString()!);
    }

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..28];

    [TestMethod]
    public async Task PlansRequireAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AdminEndpointsAreForbiddenToShoppers()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await shopper.PostAsJsonAsync("api/admin/plans", new { name = "x", price = 1, brandId = 1 });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);

        response = await shopper.PutAsJsonAsync("api/admin/subscriptions/1/next-billing-date", new { date = "2030-01-01" });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task NewPlanNeedsABrandFromTheCatalog()
    {
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var response = await admin.PostAsJsonAsync("api/admin/plans", new { name = Unique("Nobrand"), price = 5, brand = "No such brand" });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        response = await admin.PostAsJsonAsync("api/admin/plans", new { name = Unique("Noprice"), price = -1, brandId = 1 });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperRunsASubscriptionThroughItsLife()
    {
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var shopper = ClientFor(ApiTokenHelper.GetUserToken($"life-{Guid.NewGuid():N}@microsoft.com"));

        var (_, handleA) = await CreatePlanAsync(admin, Unique("Life A"), 12.5m, 1);
        var (_, handleB) = await CreatePlanAsync(admin, Unique("Life B"), 20m, 2);

        // The new plans are on offer, each under its brand.
        var plans = await ReadAsync(await shopper.GetAsync("api/plans"), HttpStatusCode.OK);
        var offered = new List<string>();
        foreach (var p in plans.GetProperty("plans").EnumerateArray())
        {
            offered.Add(p.GetProperty("handle").GetString()!);
        }
        CollectionAssert.IsSubsetOf(new[] { handleA, handleB }, offered);

        // A plan that is not on offer cannot be subscribed to.
        var response = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = "no-such-plan" });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);

        // Several subscriptions at once.
        var subA = await ReadAsync(await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handleA }), HttpStatusCode.Created);
        var subB = await ReadAsync(await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handleB }), HttpStatusCode.Created);
        var idA = subA.GetProperty("id").GetInt32();
        var idB = subB.GetProperty("id").GetInt32();
        Assert.AreNotEqual(idA, idB);
        Assert.AreEqual(handleA, subA.GetProperty("planHandle").GetString());

        // Owes money: not paid.
        var page = await ReadAsync(await shopper.GetAsync($"api/subscriptions/{idA}"), HttpStatusCode.OK);
        Assert.IsFalse(page.GetProperty("paid").GetBoolean());

        // Somebody else's subscription looks like a missing one.
        var stranger = ClientFor(ApiTokenHelper.GetUserToken($"stranger-{Guid.NewGuid():N}@microsoft.com"));
        Assert.AreEqual(HttpStatusCode.NotFound, (await stranger.GetAsync($"api/subscriptions/{idA}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await stranger.PostAsync($"api/subscriptions/{idA}/cancel", null)).StatusCode);

        // Pause and resume.
        var paused = await ReadAsync(await shopper.PostAsync($"api/subscriptions/{idA}/pause", null), HttpStatusCode.OK);
        Assert.AreEqual("on_hold", paused.GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{idA}/pause", null)).StatusCode);
        var resumed = await ReadAsync(await shopper.PostAsync($"api/subscriptions/{idA}/resume", null), HttpStatusCode.OK);
        Assert.AreEqual("active", resumed.GetProperty("state").GetString());

        // Switch plan.
        var switched = await ReadAsync(await shopper.PostAsJsonAsync($"api/subscriptions/{idA}/switch", new { planHandle = handleB }), HttpStatusCode.OK);
        Assert.AreEqual(handleB, switched.GetProperty("planHandle").GetString());

        // Cancel, switch while canceled, restart.
        var canceled = await ReadAsync(await shopper.PostAsync($"api/subscriptions/{idA}/cancel", null), HttpStatusCode.OK);
        Assert.AreEqual("canceled", canceled.GetProperty("state").GetString());
        var switchedBack = await ReadAsync(await shopper.PostAsJsonAsync($"api/subscriptions/{idA}/switch", new { planHandle = handleA }), HttpStatusCode.OK);
        Assert.AreEqual(handleA, switchedBack.GetProperty("planHandle").GetString());
        var restarted = await ReadAsync(await shopper.PostAsync($"api/subscriptions/{idA}/restart", null), HttpStatusCode.OK);
        Assert.AreEqual("active", restarted.GetProperty("state").GetString());
        Assert.AreEqual(handleA, restarted.GetProperty("planHandle").GetString());
    }

    [TestMethod]
    public async Task FreePlanIsPaidFromTheStart()
    {
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var shopper = ClientFor(ApiTokenHelper.GetUserToken($"free-{Guid.NewGuid():N}@microsoft.com"));
        var (_, handle) = await CreatePlanAsync(admin, Unique("Free"), 0m, 3);

        var sub = await ReadAsync(await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handle }), HttpStatusCode.Created);
        Assert.IsTrue(sub.GetProperty("paid").GetBoolean());
    }

    [TestMethod]
    public async Task UsageIsReportedForTheDayPicked()
    {
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var shopper = ClientFor(ApiTokenHelper.GetUserToken($"usage-{Guid.NewGuid():N}@microsoft.com"));
        var (_, handle) = await CreatePlanAsync(admin, Unique("Usage"), 3m, 1);
        var sub = await ReadAsync(await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handle }), HttpStatusCode.Created);
        var id = sub.GetProperty("id").GetInt32();

        // Another system records usage in Maxio.
        var maxio = ProgramTest.Services.GetRequiredService<MaxioAdvancedBillingClient>();
        var components = await maxio.SubscriptionComponents.ListSubscriptionComponents(new ListSubscriptionComponentsRequest { SubscriptionId = id });
        var metered = System.Linq.Enumerable.FirstOrDefault(components, c => c.Component?.Kind?.Value == "metered_component");
        if (metered?.Component?.ComponentId is not int componentId)
        {
            Assert.Inconclusive("The sandbox plan family has no metered component.");
            return;
        }
        var recorded = await maxio.SubscriptionComponents.CreateUsage(new CreateUsageOperationRequest
        {
            SubscriptionIdOrReference = id,
            ComponentId = componentId,
            Body = new CreateUsageRequest { Usage = new CreateUsage { Quantity = 5, Memo = "integration test" } },
        });
        var day = recorded.Usage.CreatedAt!.Value.ToString("yyyy-MM-dd");
        var otherDay = recorded.Usage.CreatedAt!.Value.AddDays(-3).ToString("yyyy-MM-dd");

        var usage = await ReadAsync(await shopper.GetAsync($"api/subscriptions/{id}/usage?date={day}"), HttpStatusCode.OK);
        Assert.AreEqual(5m, usage.GetProperty("totalQuantity").GetDecimal());

        usage = await ReadAsync(await shopper.GetAsync($"api/subscriptions/{id}/usage?date={otherDay}"), HttpStatusCode.OK);
        Assert.AreEqual(0m, usage.GetProperty("totalQuantity").GetDecimal());

        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"api/subscriptions/{id}/usage?date=yesterday")).StatusCode);
    }

    [TestMethod]
    public async Task StaffSetTheNextBillingDate()
    {
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var shopper = ClientFor(ApiTokenHelper.GetUserToken($"bill-{Guid.NewGuid():N}@microsoft.com"));
        var (_, handle) = await CreatePlanAsync(admin, Unique("Bill"), 7m, 1);
        var sub = await ReadAsync(await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handle }), HttpStatusCode.Created);
        var id = sub.GetProperty("id").GetInt32();

        var date = DateTime.UtcNow.AddDays(20).ToString("yyyy-MM-dd");
        var result = await ReadAsync(await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date }), HttpStatusCode.OK);
        Assert.AreEqual(date, result.GetProperty("nextBillingDate").GetString());

        var page = await ReadAsync(await shopper.GetAsync($"api/subscriptions/{id}"), HttpStatusCode.OK);
        StringAssert.StartsWith(page.GetProperty("nextBillingAt").GetString(), date);

        var past = await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date = "2020-01-01" });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, past.StatusCode);
    }

    [TestMethod]
    public async Task CurrencyPriceIsRefusedWithAReasonWhenTheSiteLacksTheCurrency()
    {
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var (planId, _) = await CreatePlanAsync(admin, Unique("Currency"), 9m, 1);

        var response = await admin.PostAsJsonAsync($"api/admin/plans/{planId}/currency-prices", new { currency = "XTS", price = 10 });
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, body);
        StringAssert.Contains(body, "XTS");

        response = await admin.PostAsJsonAsync("api/admin/plans/1/currency-prices", new { currency = "EUR", price = 10 });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
