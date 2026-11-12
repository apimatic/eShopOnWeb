using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// These tests talk to the real Maxio sandbox (credentials come from the "Maxio" configuration section,
/// i.e. user-secrets). Nothing is stubbed.
/// </summary>
[TestClass]
public class BillingEndpointsTest
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static HttpClient ClientFor(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Body(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    [TestMethod]
    public async Task PlansRequireASignedInCaller()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task StaffRoutesRefuseAShopper()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync("api/admin/plans", Body(new { name = "x", price = 1, brandId = 1 }))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync("api/admin/plans/1/currency-prices", Body(new { currency = "EUR", price = 1 }))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PutAsync("api/admin/subscriptions/1/next-billing-date", Body(new { date = "2099-01-01" }))).StatusCode);
    }

    [TestMethod]
    public async Task PlanCreationRefusesABrandOutsideTheCatalog()
    {
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var response = await admin.PostAsync("api/admin/plans", Body(new { name = "No Such Brand", price = 5, brandId = 9999 }));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UnknownPlanIsRefusedWithAReason()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await shopper.PostAsync("api/subscriptions", Body(new { planHandle = "no-such-plan-" + Guid.NewGuid().ToString("N") }));
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "not one of the plans on offer");
    }

    [TestMethod]
    public async Task ShopperSubscriptionLifecycleAgainstSandbox()
    {
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());

        // Staff add two plans under a catalog brand.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var planA = await CreatePlanAsync(admin, $"IT Plan A {suffix}", 7.5m);
        var planB = await CreatePlanAsync(admin, $"IT Plan B {suffix}", 3m);

        // Both are on offer.
        var plans = await ReadAsync(await shopper.GetAsync("api/plans"));
        StringAssert.Contains(plans.GetRawText(), planA);
        StringAssert.Contains(plans.GetRawText(), planB);

        // Subscribe (no card details).
        var created = await shopper.PostAsync("api/subscriptions", Body(new { planHandle = planA }));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var sub = await ReadAsync(created);
        var id = sub.GetProperty("id").GetInt32();
        Assert.AreEqual("active", sub.GetProperty("state").GetString());
        Assert.IsFalse(sub.GetProperty("paid").GetBoolean(), "an unpaid first invoice means the shopper still owes money");

        // Somebody else cannot see or touch it.
        var stranger = ClientFor(StrangerToken());
        Assert.AreEqual(HttpStatusCode.NotFound, (await stranger.GetAsync($"api/subscriptions/{id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await stranger.PostAsync($"api/subscriptions/{id}/cancel", null)).StatusCode);

        // Resume only works on a paused subscription.
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/resume", null)).StatusCode);

        // Pause / resume.
        Assert.AreEqual("on_hold", await StateAsync(await shopper.PostAsync($"api/subscriptions/{id}/pause", null)));
        Assert.AreEqual("active", await StateAsync(await shopper.PostAsync($"api/subscriptions/{id}/resume", null)));

        // Cancel, then switch the cancelled subscription, then restart it.
        Assert.AreEqual("canceled", await StateAsync(await shopper.PostAsync($"api/subscriptions/{id}/cancel", null)));
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/cancel", null)).StatusCode);
        var switched = await ReadAsync(await shopper.PostAsync($"api/subscriptions/{id}/switch", Body(new { planHandle = planB })));
        Assert.AreEqual(planB, switched.GetProperty("planHandle").GetString());
        Assert.AreEqual("active", await StateAsync(await shopper.PostAsync($"api/subscriptions/{id}/restart", null)));
        var shown = await ReadAsync(await shopper.GetAsync($"api/subscriptions/{id}"));
        Assert.AreEqual(planB, shown.GetProperty("planHandle").GetString());

        // Usage needs a valid day.
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"api/subscriptions/{id}/usage?date=yesterday")).StatusCode);
        var usage = await ReadAsync(await shopper.GetAsync($"api/subscriptions/{id}/usage?date=2020-01-01"));
        foreach (var component in usage.GetProperty("components").EnumerateArray())
            Assert.AreEqual(0, component.GetProperty("records").GetArrayLength(), "nothing was recorded on that day");

        // Staff move the next billing date; the date is read back from Maxio.
        var date = DateTime.UtcNow.AddDays(20).ToString("yyyy-MM-dd");
        var moved = await admin.PutAsync($"api/admin/subscriptions/{id}/next-billing-date", Body(new { date }));
        Assert.AreEqual(HttpStatusCode.OK, moved.StatusCode, await moved.Content.ReadAsStringAsync());
        StringAssert.StartsWith((await ReadAsync(moved)).GetProperty("nextBillingAt").GetString(), date);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.PutAsync($"api/admin/subscriptions/{id}/next-billing-date", Body(new { date = "2001-01-01" }))).StatusCode);
    }

    private static string StrangerToken() => TokenFor("stranger@example.com");

    private static string TokenFor(string name)
    {
        var claims = new System.Collections.Generic.List<System.Security.Claims.Claim> { new(System.Security.Claims.ClaimTypes.Name, name) };
        var key = Encoding.ASCII.GetBytes(Microsoft.eShopWeb.ApplicationCore.Constants.AuthorizationConstants.JWT_SECRET_KEY);
        var descriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key), Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature)
        };
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private static async Task<string> CreatePlanAsync(HttpClient admin, string name, decimal price)
    {
        var response = await admin.PostAsync("api/admin/plans", Body(new { name, price, brandId = 1 }));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        var plan = await ReadAsync(response);
        // The same name again is refused.
        Assert.AreEqual(HttpStatusCode.Conflict, (await admin.PostAsync("api/admin/plans", Body(new { name, price, brandId = 1 }))).StatusCode);
        return plan.GetProperty("handle").GetString()!;
    }

    private static async Task<string?> StateAsync(HttpResponseMessage r)
    {
        Assert.AreEqual(HttpStatusCode.OK, r.StatusCode, await r.Content.ReadAsStringAsync());
        return (await ReadAsync(r)).GetProperty("state").GetString();
    }

    // ---- settings: no Maxio call involved

    [TestMethod]
    public void MissingSettingsStopTheHost()
    {
        Assert.ThrowsException<InvalidOperationException>(() => MaxioServiceCollectionExtensions.Validate(new MaxioSettings()));
        Assert.ThrowsException<InvalidOperationException>(() => MaxioServiceCollectionExtensions.Validate(
            new MaxioSettings { ApiKey = "k", Subdomain = " ", Environment = "US" }));
        Assert.ThrowsException<InvalidOperationException>(() => MaxioServiceCollectionExtensions.Validate(
            new MaxioSettings { ApiKey = "k", Subdomain = "s", Environment = "MARS" }));
    }

    [TestMethod]
    public void BaseUrlOverrideIsUsedVerbatim()
    {
        var withOverride = MaxioServiceCollectionExtensions.BuildOptions(
            new MaxioSettings { ApiKey = "k", Subdomain = "s", Environment = "US", BaseUrl = "https://example.test/custom" }, NullLoggerFactory.Instance);
        Assert.AreEqual("https://example.test/custom", withOverride.Server.Production.Us.BaseUrl);

        var derived = MaxioServiceCollectionExtensions.BuildOptions(
            new MaxioSettings { ApiKey = "k", Subdomain = "mysite", Environment = "eu" }, NullLoggerFactory.Instance);
        Assert.AreEqual("mysite", derived.Server.Production.Eu.Site);
        StringAssert.Contains(derived.Server.Production.Eu.BaseUrl, "{site}");
    }
}
