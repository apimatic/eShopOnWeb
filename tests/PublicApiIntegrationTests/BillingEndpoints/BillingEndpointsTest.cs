using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// Drives the billing endpoints against the real Maxio sandbox (credentials come from the Maxio: settings,
/// i.e. user-secrets). Nothing is stubbed. Every test uses its own shopper so runs do not collide.
/// </summary>
[TestClass]
public class BillingEndpointsTest
{
    private static readonly WebApplicationFactory<Program> Factory = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string Token(string userName, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, userName) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var key = Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private static HttpClient ClientFor(string? token)
    {
        var client = Factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static HttpClient Shopper(string name) => ClientFor(Token(name));
    private static HttpClient Admin() => ClientFor(Token("admin@microsoft.com", "Administrators"));
    private static string NewShopper() => $"shopper-{Guid.NewGuid():N}@example.com";

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string> Reason(HttpResponseMessage response) =>
        (await ReadJson(response)).GetProperty("Message").GetString() ?? string.Empty;

    private static async Task<JsonElement> NewPlan(string name, decimal price, int brandId = 1)
    {
        var response = await Admin().PostAsJsonAsync("api/admin/plans", new { name, price, brandId });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await ReadJson(response);
    }

    private static async Task<JsonElement> Subscribe(HttpClient shopper, string planHandle)
    {
        var response = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await ReadJson(response);
    }

    // ------------------------------------------------------------------ access control

    [TestMethod]
    public async Task ShopperEndpointsRequireAToken()
    {
        var anonymous = ClientFor(null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("api/subscriptions", new { planHandle = "x" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/subscriptions/1")).StatusCode);
    }

    [TestMethod]
    public async Task StaffEndpointsAreForAdministratorsOnly()
    {
        var shopper = Shopper(NewShopper());
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsJsonAsync("api/admin/plans", new { name = "x", price = 1, brandId = 1 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsJsonAsync("api/admin/plans/1/currency-prices", new { currency = "EUR", price = 5 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PutAsJsonAsync("api/admin/subscriptions/1/next-billing-date", new { date = "2030-01-01" })).StatusCode);
    }

    // ------------------------------------------------------------------ plans

    [TestMethod]
    public async Task PlansListContainsANewPlanAndEveryPage()
    {
        var plan = await NewPlan($"List Check {Guid.NewGuid():N}"[..24], 4.5m);
        Assert.AreEqual(450, plan.GetProperty("priceInCents").GetInt64());
        Assert.AreEqual(1, plan.GetProperty("brandId").GetInt32());

        var response = await Shopper(NewShopper()).GetAsync("api/plans");
        response.EnsureSuccessStatusCode();
        var plans = (await ReadJson(response)).GetProperty("plans").EnumerateArray().ToList();
        Assert.IsTrue(plans.Any(p => p.GetProperty("handle").GetString() == plan.GetProperty("handle").GetString()));
    }

    [TestMethod]
    public async Task CreatingAPlanNeedsACatalogBrand()
    {
        var admin = Admin();
        var unknown = await admin.PostAsJsonAsync("api/admin/plans", new { name = "No Brand", price = 3, brandId = 9999 });
        Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(await Reason(unknown)));

        var missing = await admin.PostAsJsonAsync("api/admin/plans", new { name = "No Brand", price = 3 });
        Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);

        var byName = await admin.PostAsJsonAsync("api/admin/plans", new { name = $"By Name {Guid.NewGuid():N}"[..20], price = 2, brand = ".NET" });
        Assert.AreEqual(HttpStatusCode.Created, byName.StatusCode, await byName.Content.ReadAsStringAsync());
        Assert.AreEqual(2, (await ReadJson(byName)).GetProperty("brandId").GetInt32());
    }

    // ------------------------------------------------------------------ subscriptions

    [TestMethod]
    public async Task ShopperSubscribesToSeveralPlansAndOnlyOwnsThem()
    {
        var planA = await NewPlan($"Multi A {Guid.NewGuid():N}"[..20], 5);
        var planB = await NewPlan($"Multi B {Guid.NewGuid():N}"[..20], 7.25m, brandId: 2);
        var name = NewShopper();
        var shopper = Shopper(name);

        var first = await Subscribe(shopper, planA.GetProperty("handle").GetString()!);
        var second = await Subscribe(shopper, planB.GetProperty("handle").GetString()!);
        Assert.AreNotEqual(first.GetProperty("id").GetInt32(), second.GetProperty("id").GetInt32());

        var page = await shopper.GetAsync($"api/subscriptions/{first.GetProperty("id").GetInt32()}");
        page.EnsureSuccessStatusCode();
        var body = await ReadJson(page);
        Assert.AreEqual(planA.GetProperty("handle").GetString(), body.GetProperty("planHandle").GetString());
        // A fresh invoice is open: the shopper owes money.
        Assert.IsFalse(body.GetProperty("paid").GetBoolean());
        Assert.AreEqual(500, body.GetProperty("balanceInCents").GetInt64());

        // Somebody else cannot see it, and it is not "forbidden" either: it does not exist for them.
        var stranger = Shopper(NewShopper());
        Assert.AreEqual(HttpStatusCode.NotFound, (await stranger.GetAsync($"api/subscriptions/{first.GetProperty("id").GetInt32()}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await stranger.PostAsync($"api/subscriptions/{first.GetProperty("id").GetInt32()}/pause", null)).StatusCode);
    }

    [TestMethod]
    public async Task SubscribingToAPlanThatIsNotOnOfferIsRefusedWithAReason()
    {
        var shopper = Shopper(NewShopper());
        var response = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = "no-such-plan" });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(await Reason(response)));

        var missing = await shopper.PostAsJsonAsync("api/subscriptions", new { });
        Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [TestMethod]
    public async Task PauseResumeCancelRestartAndSwitchFollowTheSubscription()
    {
        var planA = await NewPlan($"Flow A {Guid.NewGuid():N}"[..20], 5);
        var planB = await NewPlan($"Flow B {Guid.NewGuid():N}"[..20], 9);
        var shopper = Shopper(NewShopper());
        var id = (await Subscribe(shopper, planA.GetProperty("handle").GetString()!)).GetProperty("id").GetInt32();

        async Task<JsonElement> Post(string action, object? body = null)
        {
            var response = body is null
                ? await shopper.PostAsync($"api/subscriptions/{id}/{action}", null)
                : await shopper.PostAsJsonAsync($"api/subscriptions/{id}/{action}", body);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            return await ReadJson(response);
        }

        async Task<HttpResponseMessage> Try(string action, object? body = null) =>
            body is null
                ? await shopper.PostAsync($"api/subscriptions/{id}/{action}", null)
                : await shopper.PostAsJsonAsync($"api/subscriptions/{id}/{action}", body);

        Assert.AreEqual("on_hold", (await Post("pause")).GetProperty("state").GetString());
        var pausedAgain = await Try("pause");
        Assert.AreEqual(HttpStatusCode.Conflict, pausedAgain.StatusCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(await Reason(pausedAgain)));

        Assert.AreEqual("active", (await Post("resume")).GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await Try("resume")).StatusCode);

        Assert.AreEqual("canceled", (await Post("cancel")).GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await Try("cancel")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await Try("pause")).StatusCode);

        // A canceled subscription can still be switched, and ends up on the new plan.
        var switched = await Post("switch", new { planHandle = planB.GetProperty("handle").GetString() });
        Assert.AreEqual(planB.GetProperty("handle").GetString(), switched.GetProperty("planHandle").GetString());
        var read = await ReadJson(await shopper.GetAsync($"api/subscriptions/{id}"));
        Assert.AreEqual(planB.GetProperty("handle").GetString(), read.GetProperty("planHandle").GetString());

        var restarted = await Post("restart");
        Assert.AreEqual("active", restarted.GetProperty("state").GetString());
        Assert.AreEqual(planB.GetProperty("handle").GetString(), restarted.GetProperty("planHandle").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await Try("restart")).StatusCode);

        // The new plan must be one on offer.
        var unknown = await Try("switch", new { planHandle = "no-such-plan" });
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    // ------------------------------------------------------------------ usage

    [TestMethod]
    public async Task UsageIsReportedForTheRequestedDayOnly()
    {
        var shopper = Shopper(NewShopper());
        // The sandbox's eShopSubscribe plans carry the metered "api-call" component.
        var plans = (await ReadJson(await shopper.GetAsync("api/plans"))).GetProperty("plans").EnumerateArray().ToList();
        var metered = plans.FirstOrDefault(p => p.GetProperty("handle").GetString() == "basic-plan");
        if (metered.ValueKind == JsonValueKind.Undefined)
        {
            Assert.Inconclusive("The sandbox has no plan with a metered component.");
        }

        var id = (await Subscribe(shopper, "basic-plan")).GetProperty("id").GetInt32();

        // "Another system" records usage in Maxio.
        var client = Factory.Services.GetRequiredService<MaxioAdvancedBillingClient>();
        var recorded = await client.SubscriptionComponents.CreateUsage(new CreateUsageOperationRequest
        {
            SubscriptionIdOrReference = SubscriptionIdOrReference.Int(id),
            ComponentId = ComponentIdModel.String("handle:api-call"),
            Body = new CreateUsageRequest { Usage = new MaxioAdvancedBilling.Models.CreateUsage { Quantity = 7, Memo = "integration test" } }
        });
        var day = DateOnly.FromDateTime(recorded.Usage.CreatedAt!.Value.Date);

        var today = await ReadJson(await shopper.GetAsync($"api/subscriptions/{id}/usage?date={day:yyyy-MM-dd}"));
        Assert.AreEqual(7m, today.GetProperty("totalQuantity").GetDecimal());
        Assert.AreEqual(1, today.GetProperty("components").GetArrayLength());

        foreach (var other in new[] { day.AddDays(-1), day.AddDays(1) })
        {
            var empty = await ReadJson(await shopper.GetAsync($"api/subscriptions/{id}/usage?date={other:yyyy-MM-dd}"));
            Assert.AreEqual(0m, empty.GetProperty("totalQuantity").GetDecimal(), $"usage on {other}");
        }

        var bad = await shopper.GetAsync($"api/subscriptions/{id}/usage?date=12-11-2026");
        Assert.AreEqual(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"api/subscriptions/{id}/usage")).StatusCode);
    }

    // ------------------------------------------------------------------ staff

    [TestMethod]
    public async Task StaffSetTheNextBillingDate()
    {
        var plan = await NewPlan($"Billing {Guid.NewGuid():N}"[..20], 6);
        var shopper = Shopper(NewShopper());
        var id = (await Subscribe(shopper, plan.GetProperty("handle").GetString()!)).GetProperty("id").GetInt32();
        var admin = Admin();

        var target = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(40));
        var response = await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date = target.ToString("yyyy-MM-dd") });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());

        var read = await ReadJson(await shopper.GetAsync($"api/subscriptions/{id}"));
        var next = read.GetProperty("nextBillingDate").GetDateTimeOffset();
        Assert.AreEqual(target, DateOnly.FromDateTime(next.Date));

        var past = await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date = "2020-01-01" });
        Assert.AreEqual(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(await Reason(past)));

        var invalid = await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date = "soon" });
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);

        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("api/admin/subscriptions/2147483000/next-billing-date", new { date = target.ToString("yyyy-MM-dd") })).StatusCode);

        // A canceled subscription has no next billing date to change.
        await shopper.PostAsync($"api/subscriptions/{id}/cancel", null);
        var canceled = await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date = target.AddDays(1).ToString("yyyy-MM-dd") });
        Assert.AreEqual(HttpStatusCode.Conflict, canceled.StatusCode);
    }

    [TestMethod]
    public async Task CurrencyPricesAreRefusedWithAReasonWhenMaxioCannotTakeThem()
    {
        var plan = await NewPlan($"Currency {Guid.NewGuid():N}"[..20], 8);
        var admin = Admin();
        var planId = plan.GetProperty("id").GetInt32();

        // The sandbox site defines no other currency, so Maxio refuses; the app passes the reason on as a 4xx.
        var refused = await admin.PostAsJsonAsync($"api/admin/plans/{planId}/currency-prices", new { currency = "EUR", price = 7 });
        Assert.IsTrue((int)refused.StatusCode is >= 400 and < 500, $"{(int)refused.StatusCode} {await refused.Content.ReadAsStringAsync()}");
        Assert.IsFalse(string.IsNullOrWhiteSpace(await Reason(refused)));

        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("api/admin/plans/2147483000/currency-prices", new { currency = "EUR", price = 7 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"api/admin/plans/{planId}/currency-prices", new { currency = "EURO", price = 7 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"api/admin/plans/{planId}/currency-prices", new { currency = "EUR", price = -1 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"api/admin/plans/{planId}/currency-prices", new { currency = "EUR" })).StatusCode);
    }
}
