using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PlaidEndpoints;

/// <summary>Runs against the real Plaid sandbox; credentials come from user-secrets (Plaid:*) and PLAID_* env vars.</summary>
[TestClass]
public class PlaidSandboxFlowTest
{
    private static HttpClient Client(string token)
    {
        var c = ProgramTest.NewClient;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static async Task<string> SandboxPublicTokenAsync(string institution, string product)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("PLAID-CLIENT-ID", Environment.GetEnvironmentVariable("PLAID_CLIENT_ID"));
        http.DefaultRequestHeaders.Add("PLAID-SECRET", Environment.GetEnvironmentVariable("PLAID_SECRET"));
        var res = await http.PostAsJsonAsync("https://sandbox.plaid.com/sandbox/public_token/create",
            new { institution_id = institution, initial_products = new[] { product } });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonNode>())!["public_token"]!.GetValue<string>();
    }

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonNode>())!;

    private static string[] Options(JsonNode n) => n["options"]!.AsArray().Select(o => o!.GetValue<string>()).ToArray();

    [TestMethod]
    public async Task BankPickerListsAndSearches()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var resp = await shopper.GetAsync("api/banks");
        for (var i = 0; i < 6 && resp.StatusCode == HttpStatusCode.ServiceUnavailable; i++) resp = await shopper.GetAsync("api/banks");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, await resp.Content.ReadAsStringAsync());
        var all = await ReadAsync(resp);
        Assert.IsTrue(all["banks"]!.AsArray().Count > 1000);
        var found = await ReadAsync(await shopper.GetAsync("api/banks?query=platypus"));
        Assert.IsTrue(found["banks"]!.AsArray().Any(b => b!["name"]!.GetValue<string>().Contains("Platypus")));
    }

    [TestMethod]
    public async Task PayCancelAndSupportFlow()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var admin = Client(ApiTokenHelper.GetAdminUserToken());

        var link = await shopper.PostAsJsonAsync("api/bank/link-token", new { kind = "bank" });
        Assert.AreEqual(HttpStatusCode.OK, link.StatusCode, await link.Content.ReadAsStringAsync());

        var pt = await SandboxPublicTokenAsync("ins_109508", "auth");
        var connect = await shopper.PostAsJsonAsync("api/bank/connections", new { publicToken = pt, kind = "bank" });
        Assert.AreEqual(HttpStatusCode.Created, connect.StatusCode, await connect.Content.ReadAsStringAsync());
        var connectionId = (await ReadAsync(connect))["connectionId"]!.GetValue<string>();

        var start = await shopper.PostAsJsonAsync("api/checkout/bank-payment", new
        {
            connectionId,
            items = new[] { new { catalogItemId = 1, quantity = 2 } },
            shippingCost = 5.00m,
        });
        Assert.AreEqual(HttpStatusCode.OK, start.StatusCode, await start.Content.ReadAsStringAsync());
        var started = await ReadAsync(start);
        var checkoutId = started["checkoutId"]!.GetValue<string>();
        CollectionAssert.AreEqual(new[] { "standard", "express" }, Options(started));

        var ship = await shopper.PutAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/shipping", new { shippingCost = 7.50m });
        Assert.AreEqual(HttpStatusCode.OK, ship.StatusCode);
        var newTotal = (await ReadAsync(ship))["total"]!.GetValue<decimal>();
        Assert.AreEqual(started["total"]!.GetValue<decimal>() + 2.50m, newTotal);

        var bad = await shopper.PostAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "teleport" });
        Assert.AreEqual(HttpStatusCode.BadRequest, bad.StatusCode);

        var confirm = await shopper.PostAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" });
        Assert.AreEqual(HttpStatusCode.OK, confirm.StatusCode, await confirm.Content.ReadAsStringAsync());
        var confirmation = await ReadAsync(confirm);
        Assert.AreEqual(5m, confirmation["fee"]!.GetValue<decimal>());
        Assert.IsNotNull(confirmation["arrivesOn"]);
        var orderId = confirmation["orderId"]!.GetValue<int>();

        var again = await ReadAsync(await shopper.PostAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" }));
        Assert.AreEqual(orderId, again["orderId"]!.GetValue<int>());

        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.PostAsync($"api/orders/{orderId}/cancel", null)).StatusCode);
        var cancel = await shopper.PostAsync($"api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode, await cancel.Content.ReadAsStringAsync());
        Assert.AreEqual(confirmation["charged"]!.GetValue<decimal>(), (await ReadAsync(cancel))["refunded"]!.GetValue<decimal>());

        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync("api/support/shoppers/demouser@microsoft.com/bank-activity")).StatusCode);
        var activity = await admin.GetAsync("api/support/shoppers/demouser@microsoft.com/bank-activity");
        Assert.AreEqual(HttpStatusCode.OK, activity.StatusCode, await activity.Content.ReadAsStringAsync());
        Assert.IsTrue((await ReadAsync(activity))["transactions"]!.AsArray().Count > 0);

        var from = DateTime.UtcNow.AddDays(-60).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var range = await admin.GetAsync($"api/support/shoppers/demouser@microsoft.com/transactions?from={from}&to={to}");
        Assert.AreEqual(HttpStatusCode.OK, range.StatusCode, await range.Content.ReadAsStringAsync());
        Assert.IsTrue((await ReadAsync(range))["transactions"]!.AsArray().Count > 0);
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await admin.GetAsync($"api/support/shoppers/demouser@microsoft.com/transactions?from={to}&to={from}")).StatusCode);

        Assert.AreEqual(HttpStatusCode.NoContent, (await shopper.DeleteAsync($"api/bank/connections/{connectionId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.DeleteAsync($"api/bank/connections/{connectionId}")).StatusCode);
    }

    [TestMethod]
    public async Task WholesaleOrderOffersOnlyStandard()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var pt = await SandboxPublicTokenAsync("ins_109508", "auth");
        var connect = await ReadAsync(await shopper.PostAsJsonAsync("api/bank/connections", new { publicToken = pt }));
        var start = await ReadAsync(await shopper.PostAsJsonAsync("api/checkout/bank-payment", new
        {
            connectionId = connect["connectionId"]!.GetValue<string>(),
            items = new[] { new { catalogItemId = 1, quantity = 100000 } },
            shippingCost = 0m,
        }));
        CollectionAssert.AreEqual(new[] { "standard" }, Options(start));
    }

    [TestMethod]
    public async Task PortfolioShowsOptionsAsContracts()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var pt = await SandboxPublicTokenAsync("ins_115616", "investments");
        var connect = await shopper.PostAsJsonAsync("api/bank/connections", new { publicToken = pt, kind = "brokerage" });
        Assert.AreEqual(HttpStatusCode.Created, connect.StatusCode, await connect.Content.ReadAsStringAsync());
        var portfolio = await shopper.GetAsync("api/portfolio");
        Assert.AreEqual(HttpStatusCode.OK, portfolio.StatusCode, await portfolio.Content.ReadAsStringAsync());
        var holdings = (await ReadAsync(portfolio))["holdings"]!.AsArray();
        Assert.IsTrue(holdings.Any(h => h!["contracts"] != null && h["quantity"] == null));
        Assert.IsTrue(holdings.Any(h => h!["quantity"] != null && h["contracts"] == null));
    }
}
