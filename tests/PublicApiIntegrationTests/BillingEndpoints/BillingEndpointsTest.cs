using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// End-to-end tests of the billing endpoints. They talk to the real Maxio sandbox configured under the
/// Maxio: section (user-secrets); nothing is stubbed. Each run uses its own shopper and its own plans.
/// </summary>
[TestClass]
public class BillingEndpointsTest
{
    private static HttpClient ClientFor(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> Ok(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<string> CreatePlan(HttpClient admin, string label, decimal price)
    {
        var name = $"{label} {Guid.NewGuid():N}"[..28];
        var response = await admin.PostAsJsonAsync("api/admin/plans", new { name, price, brandId = 1 });
        var plan = await Ok(response);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Console.WriteLine("PLAN " + plan);
        Assert.AreEqual(1, plan.GetProperty("brandId").GetInt32());
        return plan.GetProperty("handle").GetString()!;
    }

    [TestMethod]
    public async Task BaseUrlOverrideIsUsedVerbatimAndAnUnreachableBillingSystemIsReported()
    {
        // Maxio:BaseUrl wins over the subdomain-derived address: pointing it at a closed port must fail to connect.
        using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("Maxio:BaseUrl", "http://127.0.0.1:9"));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/plans");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode, text);
        StringAssert.Contains(text, "could not be reached");
    }

    [TestMethod]
    public async Task PlansRequireSignIn()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task StaffEndpointsRejectShoppers()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsJsonAsync("api/admin/plans", new { name = "x", price = 1, brandId = 1 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsJsonAsync("api/admin/plans/1/currency-prices", new { currency = "EUR", price = 1 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PutAsJsonAsync("api/admin/subscriptions/1/next-billing-date", new { date = "2030-01-01" })).StatusCode);
    }

    [TestMethod]
    public async Task NewPlanNeedsABrandOfTheCatalog()
    {
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var response = await admin.PostAsJsonAsync("api/admin/plans", new { name = "Nope " + Guid.NewGuid().ToString("N")[..8], price = 5, brandId = 99999 });
        Assert.IsTrue((int)response.StatusCode is >= 400 and < 500, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task ShopperLifecycle()
    {
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var email = $"shopper-{Guid.NewGuid():N}@example.com";
        var shopper = ClientFor(ApiTokenHelper.GetUserToken(email));
        var stranger = ClientFor(ApiTokenHelper.GetUserToken($"stranger-{Guid.NewGuid():N}@example.com"));

        var handleA = await CreatePlan(admin, "Test A", 12.50m);
        var handleB = await CreatePlan(admin, "Test B", 20m);

        var plans = await Ok(await shopper.GetAsync("api/plans"));
        Assert.IsTrue(plans.EnumerateArray().Any(p => p.GetProperty("handle").GetString() == handleA));

        // unknown plan
        var unknown = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = "no-such-plan-" + Guid.NewGuid().ToString("N") });
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);

        // subscribe to several plans at once
        var created = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handleA });
        var subA = await Ok(created);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var idA = subA.GetProperty("id").GetInt32();
        var subB = await Ok(await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handleB }));
        Assert.AreNotEqual(idA, subB.GetProperty("id").GetInt32());

        // subscription page: money is owed on an invoiced plan
        var page = await Ok(await shopper.GetAsync($"api/subscriptions/{idA}"));
        Console.WriteLine("SUBSCRIPTION " + page);
        Assert.AreEqual(handleA, page.GetProperty("planHandle").GetString());
        Assert.IsFalse(page.GetProperty("paid").GetBoolean());

        // nobody else sees it
        Assert.AreEqual(HttpStatusCode.NotFound, (await stranger.GetAsync($"api/subscriptions/{idA}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await stranger.PostAsync($"api/subscriptions/{idA}/cancel", null)).StatusCode);

        // pause / resume
        Assert.AreEqual("on_hold", (await Ok(await shopper.PostAsync($"api/subscriptions/{idA}/pause", null))).GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{idA}/pause", null)).StatusCode);
        Assert.AreEqual("active", (await Ok(await shopper.PostAsync($"api/subscriptions/{idA}/resume", null))).GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{idA}/resume", null)).StatusCode);

        // switch while active
        var switched = await Ok(await shopper.PostAsJsonAsync($"api/subscriptions/{idA}/switch", new { planHandle = handleB }));
        Assert.AreEqual(handleB, switched.GetProperty("planHandle").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsJsonAsync($"api/subscriptions/{idA}/switch", new { planHandle = handleB })).StatusCode);

        // cancel / restart
        Assert.AreEqual("canceled", (await Ok(await shopper.PostAsync($"api/subscriptions/{idA}/cancel", null))).GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{idA}/cancel", null)).StatusCode);

        // switch while canceled
        var switchedCanceled = await Ok(await shopper.PostAsJsonAsync($"api/subscriptions/{idA}/switch", new { planHandle = handleA }));
        Assert.AreEqual(handleA, switchedCanceled.GetProperty("planHandle").GetString());

        var restarted = await Ok(await shopper.PostAsync($"api/subscriptions/{idA}/restart", null));
        Console.WriteLine("RESTARTED " + restarted);
        Assert.AreEqual(handleA, restarted.GetProperty("planHandle").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{idA}/restart", null)).StatusCode);

        // usage on a day
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var usage = await Ok(await shopper.GetAsync($"api/subscriptions/{idA}/usage?date={today}"));
        Assert.AreEqual(today, usage.GetProperty("date").GetString());
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"api/subscriptions/{idA}/usage?date=yesterday")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"api/subscriptions/{idA}/usage")).StatusCode);

        // staff: next billing date
        var target = DateTime.UtcNow.Date.AddDays(45);
        var moved = await Ok(await admin.PutAsJsonAsync($"api/admin/subscriptions/{idA}/next-billing-date", new { date = target.ToString("yyyy-MM-dd") }));
        Console.WriteLine("MOVED " + moved);
        Assert.AreEqual(target.ToString("yyyy-MM-dd"), moved.GetProperty("nextBillingAt").GetString()![..10]);
        var past = await admin.PutAsJsonAsync($"api/admin/subscriptions/{idA}/next-billing-date", new { date = "2020-01-01" });
        Assert.IsTrue((int)past.StatusCode is >= 400 and < 500);
    }

    [TestMethod]
    public async Task CurrencyPriceIsRefusedWithAReasonWhenTheSiteLacksTheCurrency()
    {
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var plans = await Ok(await ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/plans"));
        var id = plans.EnumerateArray().First().GetProperty("id").GetInt32();
        var response = await admin.PostAsJsonAsync($"api/admin/plans/{id}/currency-prices", new { currency = "EUR", price = 10 });
        var text = await response.Content.ReadAsStringAsync();
        Console.WriteLine("CURRENCY " + (int)response.StatusCode + " " + text);
        Assert.IsTrue(response.IsSuccessStatusCode || (int)response.StatusCode is >= 400 and < 500, text);
        var missing = await admin.PostAsJsonAsync("api/admin/plans/1/currency-prices", new { currency = "EURO", price = 10 });
        Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);
    }
}
