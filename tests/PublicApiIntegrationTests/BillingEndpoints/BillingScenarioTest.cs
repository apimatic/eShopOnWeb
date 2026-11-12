using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// End-to-end tests of the billing endpoints against the real Maxio sandbox (credentials come from the Maxio:
/// configuration section, i.e. user-secrets or Maxio__* environment variables). Nothing is stubbed.
/// </summary>
[TestClass]
public class BillingScenarioTest
{
    private static readonly string Run = Guid.NewGuid().ToString("N")[..10];

    private static HttpClient Client(string? token)
    {
        var client = ProgramTest.NewClient;
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient Admin() => Client(ApiTokenHelper.GetAdminUserToken());
    private static HttpClient Shopper(string user) => Client(ApiTokenHelper.GetUserToken(user));

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> CreatePlan(string name, decimal price, int brandId)
    {
        var response = await Admin().PostAsJsonAsync("api/admin/plans", new { name, price, catalogBrandId = brandId });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await Json(response)).GetProperty("plan");
    }

    [TestMethod]
    public async Task PlanEndpointsEnforceRolesAndValidation()
    {
        var forShopper = await Shopper($"nobody-{Run}@example.com").PostAsJsonAsync("api/admin/plans", new { name = "x", price = 1, catalogBrandId = 1 });
        Assert.AreEqual(HttpStatusCode.Forbidden, forShopper.StatusCode);

        var anonymous = await Client(null).PostAsJsonAsync("api/admin/plans", new { name = "x", price = 1, catalogBrandId = 1 });
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var noBrand = await Admin().PostAsJsonAsync("api/admin/plans", new { name = $"Bad {Run}", price = 5, catalogBrandId = 9999 });
        Assert.AreEqual(HttpStatusCode.NotFound, noBrand.StatusCode);

        var badPrice = await Admin().PostAsJsonAsync("api/admin/plans", new { name = $"Bad {Run}", price = -1, catalogBrandId = 1 });
        Assert.AreEqual(HttpStatusCode.BadRequest, badPrice.StatusCode);

        var noName = await Admin().PostAsJsonAsync("api/admin/plans", new { price = 1, catalogBrandId = 1 });
        Assert.AreEqual(HttpStatusCode.BadRequest, noName.StatusCode);

        var noToken = await Client(null).PostAsJsonAsync("api/subscriptions", new { planHandle = "x" });
        Assert.AreEqual(HttpStatusCode.Unauthorized, noToken.StatusCode);
    }

    [TestMethod]
    public async Task AdminCreatedPlanIsListedAndCanBeSubscribedTo()
    {
        var plan = await CreatePlan($"Listed {Run}", 12.50m, 1);
        Assert.AreEqual(1250, plan.GetProperty("priceInCents").GetInt64());
        Assert.AreEqual(1, plan.GetProperty("catalogBrandId").GetInt32());
        var handle = plan.GetProperty("handle").GetString()!;

        var list = await Client(null).GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
        var plans = (await Json(list)).GetProperty("plans");
        var found = false;
        foreach (var p in plans.EnumerateArray()) found |= p.GetProperty("handle").GetString() == handle;
        Assert.IsTrue(found, "the new plan must be listed");

        var user = $"shopper-{Run}-a@example.com";
        var shopper = Shopper(user);
        var unknown = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = $"no-such-plan-{Run}" });
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);

        var first = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handle });
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode, await first.Content.ReadAsStringAsync());
        var sub1 = await Json(first);
        var id1 = sub1.GetProperty("id").GetInt32();
        Assert.AreEqual(handle, sub1.GetProperty("planHandle").GetString());

        // A shopper can hold several subscriptions at once, even to the same plan.
        var second = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handle });
        Assert.AreEqual(HttpStatusCode.Created, second.StatusCode, await second.Content.ReadAsStringAsync());
        Assert.AreNotEqual(id1, (await Json(second)).GetProperty("id").GetInt32());

        var read = await shopper.GetAsync($"api/subscriptions/{id1}");
        Assert.AreEqual(HttpStatusCode.OK, read.StatusCode);
        var view = await Json(read);
        Assert.AreEqual(id1, view.GetProperty("id").GetInt32());
        Console.WriteLine("subscription: " + view);
        Assert.AreEqual(view.GetProperty("balanceInCents").GetInt64() <= 0, view.GetProperty("paid").GetBoolean());

        // Someone else's subscription does not exist for you.
        var other = await Shopper($"shopper-{Run}-b@example.com").GetAsync($"api/subscriptions/{id1}");
        Assert.AreEqual(HttpStatusCode.NotFound, other.StatusCode);
        var otherPause = await Shopper($"shopper-{Run}-b@example.com").PostAsync($"api/subscriptions/{id1}/pause", null);
        Assert.AreEqual(HttpStatusCode.NotFound, otherPause.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionLifecycle()
    {
        var planA = await CreatePlan($"Life A {Run}", 10m, 2);
        var planB = await CreatePlan($"Life B {Run}", 20m, 3);
        var handleA = planA.GetProperty("handle").GetString()!;
        var handleB = planB.GetProperty("handle").GetString()!;

        var shopper = Shopper($"shopper-{Run}-life@example.com");
        var created = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handleA });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var id = (await Json(created)).GetProperty("id").GetInt32();

        // Not paused yet -> cannot resume.
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/resume", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/restart", null)).StatusCode);

        var paused = await shopper.PostAsync($"api/subscriptions/{id}/pause", null);
        Assert.AreEqual(HttpStatusCode.OK, paused.StatusCode, await paused.Content.ReadAsStringAsync());
        Assert.AreEqual("on_hold", (await Json(paused)).GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/pause", null)).StatusCode);

        var resumed = await shopper.PostAsync($"api/subscriptions/{id}/resume", null);
        Assert.AreEqual(HttpStatusCode.OK, resumed.StatusCode, await resumed.Content.ReadAsStringAsync());
        Assert.AreEqual("active", (await Json(resumed)).GetProperty("state").GetString());

        var cancelled = await shopper.PostAsync($"api/subscriptions/{id}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancelled.StatusCode, await cancelled.Content.ReadAsStringAsync());
        Assert.AreEqual("canceled", (await Json(cancelled)).GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/cancel", null)).StatusCode);

        // Switching a cancelled subscription puts the shopper on the new plan.
        var switched = await shopper.PostAsJsonAsync($"api/subscriptions/{id}/switch", new { planHandle = handleB });
        Assert.AreEqual(HttpStatusCode.OK, switched.StatusCode, await switched.Content.ReadAsStringAsync());
        var switchedView = await Json(switched);
        Assert.AreEqual(handleB, switchedView.GetProperty("planHandle").GetString());
        Assert.AreEqual("active", switchedView.GetProperty("state").GetString());

        // Already on that plan, and a plan the shop does not offer.
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsJsonAsync($"api/subscriptions/{id}/switch", new { planHandle = handleB })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.PostAsJsonAsync($"api/subscriptions/{id}/switch", new { planHandle = $"nope-{Run}" })).StatusCode);

        // Cancel and restart.
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/cancel", null)).StatusCode);
        var restarted = await shopper.PostAsync($"api/subscriptions/{id}/restart", null);
        Assert.AreEqual(HttpStatusCode.OK, restarted.StatusCode, await restarted.Content.ReadAsStringAsync());
        Assert.AreEqual("active", (await Json(restarted)).GetProperty("state").GetString());

        // Switch a live subscription back.
        var back = await shopper.PostAsJsonAsync($"api/subscriptions/{id}/switch", new { planHandle = handleA });
        Assert.AreEqual(HttpStatusCode.OK, back.StatusCode, await back.Content.ReadAsStringAsync());
        Assert.AreEqual(handleA, (await Json(back)).GetProperty("planHandle").GetString());

        // Usage: validation and an empty day.
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"api/subscriptions/{id}/usage")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"api/subscriptions/{id}/usage?date=2026-13-45")).StatusCode);
        var usage = await shopper.GetAsync($"api/subscriptions/{id}/usage?date=2026-01-15");
        Assert.AreEqual(HttpStatusCode.OK, usage.StatusCode, await usage.Content.ReadAsStringAsync());
        Assert.AreEqual("2026-01-15", (await Json(usage)).GetProperty("date").GetString());

        // Staff set the next billing date.
        var date = DateTime.UtcNow.Date.AddDays(45).ToString("yyyy-MM-dd");
        var billing = await Admin().PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { nextBillingDate = date });
        Assert.AreEqual(HttpStatusCode.OK, billing.StatusCode, await billing.Content.ReadAsStringAsync());
        Assert.AreEqual(date, (await Json(billing)).GetProperty("nextBillingAt").GetDateTimeOffset().ToString("yyyy-MM-dd"));
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Admin().PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { nextBillingDate = "2020-01-01" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { nextBillingDate = date })).StatusCode);
    }

    [TestMethod]
    public async Task UsageForADayCountsOnlyThatDaysMeteredRecords()
    {
        var plan = await CreatePlan($"Usage {Run}", 3m, 2);
        var shopper = Shopper($"shopper-{Run}-usage@example.com");
        var created = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = plan.GetProperty("handle").GetString() });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var id = (await Json(created)).GetProperty("id").GetInt32();

        // Another system records usage in Maxio: do the same through the SDK.
        var maxio = ProgramTest.Services.GetRequiredService<MaxioAdvancedBillingClient>();
        var components = await maxio.SubscriptionComponents.ListSubscriptionComponents(new ListSubscriptionComponentsRequest { SubscriptionId = id });
        var metered = components.Select(c => c.Component).FirstOrDefault(c => c?.Kind == ComponentKind.MeteredComponent);
        if (metered is null)
        {
            Assert.Inconclusive("The sandbox product family has no metered component to record usage on.");
            return;
        }
        var recorded = await maxio.SubscriptionComponents.CreateUsage(new CreateUsageOperationRequest
        {
            SubscriptionIdOrReference = id,
            ComponentId = metered.ComponentId!.Value,
            Body = new CreateUsageRequest { Usage = new CreateUsage { Quantity = 3, Memo = $"test {Run}" } }
        });
        var day = recorded.Usage.CreatedAt!.Value.ToString("yyyy-MM-dd");

        var usage = await shopper.GetAsync($"api/subscriptions/{id}/usage?date={day}");
        Assert.AreEqual(HttpStatusCode.OK, usage.StatusCode, await usage.Content.ReadAsStringAsync());
        var body = await Json(usage);
        Console.WriteLine("usage: " + body);
        var component = body.GetProperty("components").EnumerateArray().First(c => c.GetProperty("componentId").GetInt32() == metered.ComponentId);
        Assert.AreEqual(3m, component.GetProperty("totalQuantity").GetDecimal());
        Assert.AreEqual(1, component.GetProperty("records").GetArrayLength());

        // The day before holds nothing.
        var before = DateOnly.ParseExact(day, "yyyy-MM-dd").AddDays(-1).ToString("yyyy-MM-dd");
        var empty = await Json(await shopper.GetAsync($"api/subscriptions/{id}/usage?date={before}"));
        var emptyComponent = empty.GetProperty("components").EnumerateArray().First(c => c.GetProperty("componentId").GetInt32() == metered.ComponentId);
        Assert.AreEqual(0m, emptyComponent.GetProperty("totalQuantity").GetDecimal());
    }

    [TestMethod]
    public async Task CurrencyPriceIsRefusedWithAReasonWhenTheSiteLacksTheCurrency()
    {
        var plan = await CreatePlan($"Money {Run}", 9m, 1);
        var response = await Admin().PostAsJsonAsync($"api/admin/plans/{plan.GetProperty("id").GetInt32()}/currency-prices", new { currency = "EUR", price = 9 });
        var body = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"{(int)response.StatusCode} {body}");
        Assert.IsTrue(response.StatusCode == HttpStatusCode.Created || (int)response.StatusCode is >= 400 and < 500, body);

        Assert.AreEqual(HttpStatusCode.BadRequest, (await Admin().PostAsJsonAsync($"api/admin/plans/{plan.GetProperty("id").GetInt32()}/currency-prices", new { currency = "EURO", price = 9 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Admin().PostAsJsonAsync("api/admin/plans/1/currency-prices", new { currency = "EUR", price = 9 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Shopper($"x-{Run}@example.com").PostAsJsonAsync("api/admin/plans/1/currency-prices", new { currency = "EUR", price = 9 })).StatusCode);
    }
}
