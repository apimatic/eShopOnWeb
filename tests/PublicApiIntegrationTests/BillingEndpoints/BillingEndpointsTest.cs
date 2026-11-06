using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// End-to-end tests against the real Maxio sandbox (credentials come from user-secrets).
/// Every test uses its own shopper so runs never interfere with each other.
/// </summary>
[TestClass]
public class BillingEndpointsTest
{
    private static readonly string Run = Guid.NewGuid().ToString("N")[..8];
    private static readonly List<(string user, int id)> Created = new();

    private static HttpClient Client(string? token)
    {
        var client = ProgramTest.NewClient;
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient Shopper(string name) => Client(ApiTokenHelper.GetShopperToken(name));
    private static HttpClient Admin() => Client(ApiTokenHelper.GetAdminUserToken());
    private static string NewUser(string tag) => $"it-{tag}-{Run}@example.com";

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(text.Length > 0, $"empty body, status {response.StatusCode}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> CreatePlan(string name, decimal price = 12.50m, string brand = "Azure")
    {
        var response = await Admin().PostAsJsonAsync("api/admin/plans", new { name, monthlyPrice = price, brand });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    private static async Task<JsonElement> Subscribe(HttpClient client, string user, string handle)
    {
        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = handle });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        Created.Add((user, body.GetProperty("subscriptionId").GetInt32()));
        return body;
    }

    [ClassCleanup]
    public static async Task Cleanup()
    {
        foreach (var (user, id) in Created)
        {
            try { await Shopper(user).PostAsync($"api/subscriptions/{id}/cancel", null); } catch { /* best effort */ }
        }
    }

    [TestMethod]
    public async Task PlansListsOnlyAvailablePlans()
    {
        var plan = await CreatePlan($"List Probe {Run}");
        var response = await Shopper(NewUser("list")).GetAsync("api/plans");
        response.EnsureSuccessStatusCode();
        var body = await Json(response);
        var handles = new List<string>();
        foreach (var p in body.GetProperty("plans").EnumerateArray()) handles.Add(p.GetProperty("handle").GetString()!);
        CollectionAssert.Contains(handles, plan.GetProperty("handle").GetString());
        Assert.AreEqual("Azure", plan.GetProperty("brand").GetString());
        Assert.AreEqual(12.50m, plan.GetProperty("monthlyPrice").GetDecimal());
    }

    [TestMethod]
    public async Task AdminEndpointsRejectShoppersAndAnonymous()
    {
        var body = new { name = "x", monthlyPrice = 1, brand = "Azure" };
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Shopper(NewUser("rbac")).PostAsJsonAsync("api/admin/plans", body)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Client(null).PostAsJsonAsync("api/admin/plans", body)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Shopper(NewUser("rbac")).PutAsJsonAsync("api/admin/subscriptions/1/next-billing-date", new { date = "2030-01-01" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Shopper(NewUser("rbac")).PostAsJsonAsync("api/admin/plans/1/currency-prices", new { currency = "EUR", price = 1 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Client(null).PostAsJsonAsync("api/subscriptions", new { planHandle = "x" })).StatusCode);
    }

    [TestMethod]
    public async Task CreatePlanValidatesBrandAndDuplicates()
    {
        var bad = await Admin().PostAsJsonAsync("api/admin/plans", new { name = $"Bad Brand {Run}", monthlyPrice = 5, brand = "No Such Brand" });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        var neg = await Admin().PostAsJsonAsync("api/admin/plans", new { name = $"Negative {Run}", monthlyPrice = -5, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.BadRequest, neg.StatusCode);

        await CreatePlan($"Dup Probe {Run}");
        var dup = await Admin().PostAsJsonAsync("api/admin/plans", new { name = $"Dup Probe {Run}", monthlyPrice = 5, brand = ".NET" });
        Assert.AreEqual(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeShowsUnpaidAndIsPrivate()
    {
        var plan = await CreatePlan($"Sub Probe {Run}", 9.99m, "Visual Studio");
        var user = NewUser("sub");
        var client = Shopper(user);
        var sub = await Subscribe(client, user, plan.GetProperty("handle").GetString()!);
        Assert.IsFalse(sub.GetProperty("paid").GetBoolean(), "shopper owes the first invoice");

        var id = sub.GetProperty("subscriptionId").GetInt32();
        var read = await Json(await client.GetAsync($"api/subscriptions/{id}"));
        Assert.IsFalse(read.GetProperty("paid").GetBoolean());
        Assert.AreEqual(plan.GetProperty("handle").GetString(), read.GetProperty("planHandle").GetString());

        // someone else's subscription looks like it does not exist
        Assert.AreEqual(HttpStatusCode.NotFound, (await Shopper(NewUser("other")).GetAsync($"api/subscriptions/{id}")).StatusCode);

        // several plans at once
        var second = await CreatePlan($"Sub Probe 2 {Run}", 3m, ".NET");
        var sub2 = await Subscribe(client, user, second.GetProperty("handle").GetString()!);
        Assert.AreNotEqual(id, sub2.GetProperty("subscriptionId").GetInt32());

        // free plans are paid
        var free = await CreatePlan($"Free Probe {Run}", 0m, "Other");
        var freeSub = await Subscribe(client, user, free.GetProperty("handle").GetString()!);
        Assert.IsTrue(freeSub.GetProperty("paid").GetBoolean());

        Assert.AreEqual(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "nope-" + Run })).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("api/subscriptions", new { })).StatusCode);
    }

    [TestMethod]
    public async Task PauseResumeCancelRestartAndSwitch()
    {
        var a = await CreatePlan($"Life A {Run}", 10m);
        var b = await CreatePlan($"Life B {Run}", 20m, ".NET");
        var handleA = a.GetProperty("handle").GetString()!;
        var handleB = b.GetProperty("handle").GetString()!;
        var user = NewUser("life");
        var client = Shopper(user);
        var id = (await Subscribe(client, user, handleA)).GetProperty("subscriptionId").GetInt32();
        string Route(string action) => $"api/subscriptions/{id}/{action}";

        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsync(Route("resume"), null)).StatusCode, "cannot resume an active subscription");

        var paused = await client.PostAsync(Route("pause"), null);
        Assert.AreEqual(HttpStatusCode.OK, paused.StatusCode, await paused.Content.ReadAsStringAsync());
        Assert.AreEqual("on_hold", (await Json(paused)).GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsync(Route("pause"), null)).StatusCode);

        var resumed = await Json(await client.PostAsync(Route("resume"), null));
        Assert.AreEqual("active", resumed.GetProperty("state").GetString());

        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsync(Route("restart"), null)).StatusCode, "cannot restart an active subscription");
        var cancelled = await Json(await client.PostAsync(Route("cancel"), null));
        Assert.AreEqual("canceled", cancelled.GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsync(Route("cancel"), null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsync(Route("pause"), null)).StatusCode);

        // switch while cancelled: ends up on the new plan
        var switched = await client.PostAsJsonAsync(Route("switch"), new { planHandle = handleB });
        Assert.AreEqual(HttpStatusCode.OK, switched.StatusCode, await switched.Content.ReadAsStringAsync());
        var sw = await Json(switched);
        Assert.AreEqual(handleB, sw.GetProperty("planHandle").GetString());
        Assert.AreEqual("active", sw.GetProperty("state").GetString());

        // switch while active, back to A
        var back = await Json(await client.PostAsJsonAsync(Route("switch"), new { planHandle = handleA }));
        Assert.AreEqual(handleA, back.GetProperty("planHandle").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Route("switch"), new { planHandle = handleA })).StatusCode, "already on that plan");
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(Route("switch"), new { planHandle = "nope-" + Run })).StatusCode);

        // cancel then restart
        await client.PostAsync(Route("cancel"), null);
        var restarted = await Json(await client.PostAsync(Route("restart"), null));
        Assert.AreEqual("active", restarted.GetProperty("state").GetString());

        // another shopper cannot touch it
        Assert.AreEqual(HttpStatusCode.NotFound, (await Shopper(NewUser("thief")).PostAsync(Route("cancel"), null)).StatusCode);
    }

    [TestMethod]
    public async Task UsageForASingleDay()
    {
        // metered usage is recorded by "another system": do the same here straight against Maxio
        using var http = new HttpClient();
        var maxio = MaxioClientFactory.Create(ProgramTest.Configuration, http);
        var components = await maxio.Components.ListComponents(new MaxioAdvancedBilling.Requests.Components.ListComponentsRequest { PerPage = 200 });
        ComponentResponse? metered = null;
        foreach (var c in components)
            if (c.Component.Kind == MaxioAdvancedBilling.Models.Enums.ComponentKind.MeteredComponent && c.Component.Archived != true) { metered = c; break; }
        Assert.IsNotNull(metered, "the sandbox needs one metered component");
        var familyId = metered!.Component.ProductFamilyId;

        // a plan in that product family
        var products = await maxio.Products.ListProducts(new MaxioAdvancedBilling.Requests.Products.ListProductsRequest { PerPage = 200 });
        string? handle = null;
        foreach (var p in products)
            if (p.Product.ProductFamily?.Id == familyId && p.Product.ArchivedAt == null && !string.IsNullOrEmpty(p.Product.Handle)) { handle = p.Product.Handle; break; }
        Assert.IsNotNull(handle, "the sandbox needs a plan in the metered component's family");

        var user = NewUser("usage");
        var client = Shopper(user);
        var id = (await Subscribe(client, user, handle!)).GetProperty("subscriptionId").GetInt32();

        await maxio.SubscriptionComponents.CreateUsage(new CreateUsageOperationRequest
        {
            SubscriptionIdOrReference = id,
            ComponentId = metered.Component.Id!.Value,
            Body = new CreateUsageRequest { Usage = new CreateUsage { Quantity = 5, Memo = "integration test" } },
        });
        await maxio.SubscriptionComponents.CreateUsage(new CreateUsageOperationRequest
        {
            SubscriptionIdOrReference = id,
            ComponentId = metered.Component.Id!.Value,
            Body = new CreateUsageRequest { Usage = new CreateUsage { Quantity = 3, Memo = "integration test" } },
        });

        // the site's "today" is what Maxio stamped on the usage
        var sub = await maxio.Subscriptions.ReadSubscription(new MaxioAdvancedBilling.Requests.Subscriptions.ReadSubscriptionRequest { SubscriptionId = id });
        var today = DateOnly.FromDateTime(sub.Subscription!.CreatedAt!.Value.DateTime);
        var yesterday = today.AddDays(-1);
        var tomorrow = today.AddDays(1);

        var usage = await Json(await client.GetAsync($"api/subscriptions/{id}/usage?date={today:yyyy-MM-dd}"));
        Assert.AreEqual(8m, usage.GetProperty("totalQuantity").GetDecimal());
        Assert.AreEqual(1, usage.GetProperty("components").GetArrayLength());
        Assert.AreEqual(2, usage.GetProperty("components")[0].GetProperty("usages").GetArrayLength());

        foreach (var other in new[] { yesterday, tomorrow })
        {
            var empty = await Json(await client.GetAsync($"api/subscriptions/{id}/usage?date={other:yyyy-MM-dd}"));
            Assert.AreEqual(0m, empty.GetProperty("totalQuantity").GetDecimal(), other.ToString());
        }

        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.GetAsync($"api/subscriptions/{id}/usage")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.GetAsync($"api/subscriptions/{id}/usage?date=06/11/2026")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Shopper(NewUser("snoop")).GetAsync($"api/subscriptions/{id}/usage?date={today:yyyy-MM-dd}")).StatusCode);
    }

    [TestMethod]
    public async Task NextBillingDateIsAppliedInTheSitesTimeZone()
    {
        var plan = await CreatePlan($"Bill Probe {Run}", 7m, "SQL Server");
        var user = NewUser("bill");
        var id = (await Subscribe(Shopper(user), user, plan.GetProperty("handle").GetString()!)).GetProperty("subscriptionId").GetInt32();

        var target = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(40);
        var admin = Admin();
        var response = await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date = target.ToString("yyyy-MM-dd") });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(target.ToString("yyyy-MM-dd"), (await Json(response)).GetProperty("nextBillingDate").GetString());

        // and what the shopper sees agrees
        var read = await Json(await Shopper(user).GetAsync($"api/subscriptions/{id}"));
        Assert.AreEqual(target.ToString("yyyy-MM-dd"), read.GetProperty("nextBillingDate").GetString());

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, (await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date = "2020-01-01" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date = "soon" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("api/admin/subscriptions/2147483000/next-billing-date", new { date = target.ToString("yyyy-MM-dd") })).StatusCode);
    }

    [TestMethod]
    public async Task CurrencyPriceIsAcceptedOnlyForCurrenciesEnabledOnTheSite()
    {
        var plan = await CreatePlan($"Cur Probe {Run}", 11m, "Azure");
        var planId = plan.GetProperty("planId").GetInt32();

        var response = await Admin().PostAsJsonAsync($"api/admin/plans/{planId}/currency-prices", new { currency = "EUR", price = 10 });
        // 201 when the sandbox site has EUR enabled, otherwise a 4xx that explains why
        if (response.StatusCode != HttpStatusCode.Created)
        {
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            StringAssert.Contains((await Json(response)).GetProperty("Message").GetString(), "EUR");
        }

        Assert.AreEqual(HttpStatusCode.BadRequest, (await Admin().PostAsJsonAsync($"api/admin/plans/{planId}/currency-prices", new { currency = "E", price = 10 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Admin().PostAsJsonAsync("api/admin/plans/2147483000/currency-prices", new { currency = "EUR", price = 10 })).StatusCode);
    }
}
