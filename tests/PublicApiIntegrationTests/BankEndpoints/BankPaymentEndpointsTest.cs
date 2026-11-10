using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BankEndpoints;

/// <summary>
/// Runs against the real Plaid sandbox. The credentials come from the PLAID_CLIENT_ID / PLAID_SECRET /
/// PLAID_ENV environment variables (and the PublicApi's user-secrets); nothing is stubbed.
/// </summary>
[TestClass]
public class BankPaymentEndpointsTest
{
    private static readonly HttpClient Plaid = new();

    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> SandboxPublicTokenAsync(string institutionId, params string[] products)
    {
        var clientId = Environment.GetEnvironmentVariable("PLAID_CLIENT_ID");
        var secret = Environment.GetEnvironmentVariable("PLAID_SECRET");
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(secret))
        {
            Assert.Inconclusive("PLAID_CLIENT_ID / PLAID_SECRET are not set.");
        }

        var env = Environment.GetEnvironmentVariable("PLAID_ENV") ?? "sandbox";
        var body = new JsonObject
        {
            ["client_id"] = clientId,
            ["secret"] = secret,
            ["institution_id"] = institutionId,
            ["initial_products"] = new JsonArray(products.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray())
        };
        var response = await Plaid.PostAsync($"https://{env}.plaid.com/sandbox/public_token/create",
            new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        return json["public_token"]!.GetValue<string>();
    }

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, response.StatusCode, text);
        return string.IsNullOrEmpty(text) ? new JsonObject() : JsonNode.Parse(text)!;
    }

    private static async Task<int> ConnectBankAsync(HttpClient client, string type = "bank", string institution = "ins_109508", string product = "auth")
    {
        var publicToken = await SandboxPublicTokenAsync(institution, product);
        var connected = await ReadAsync(
            await client.PostAsJsonAsync("/api/bank/connections", new { publicToken, type }), HttpStatusCode.Created);
        Assert.IsNull(connected["accessToken"]);
        return connected["connectionId"]!.GetValue<int>();
    }

    [TestMethod]
    public async Task BankPickerListsAndSearchesBanks()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var all = await ReadAsync(await client.GetAsync("/api/banks?count=50"), HttpStatusCode.OK);
        Assert.AreEqual(50, all["banks"]!.AsArray().Count);
        Assert.IsTrue(all["total"]!.GetValue<int>() > 1000);
        // a second open of the picker is answered from the cache
        await ReadAsync(await client.GetAsync("/api/banks?count=50"), HttpStatusCode.OK);

        var found = await ReadAsync(await client.GetAsync("/api/banks?query=First%20Platypus"), HttpStatusCode.OK);
        Assert.IsTrue(found["banks"]!.AsArray().Any(b => b!["institutionId"]!.GetValue<string>() == "ins_109508"));
    }

    [TestMethod]
    public async Task LinkTokenIsCreatedForBankAndBrokerage()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        foreach (var type in new[] { "bank", "brokerage" })
        {
            var json = await ReadAsync(await client.PostAsJsonAsync("/api/bank/link-token", new { type }), HttpStatusCode.OK);
            Assert.IsTrue(json["linkToken"]!.GetValue<string>().StartsWith("link-"));
        }
    }

    [TestMethod]
    public async Task EndpointsRequireAuthenticationAndRole()
    {
        var anonymous = ProgramTest.NewClient;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/bank/link-token", new { })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/portfolio")).StatusCode);

        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.GetAsync("/api/support/shoppers/demouser@microsoft.com/bank-activity")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.GetAsync("/api/support/shoppers/demouser@microsoft.com/transactions?from=2026-01-01&to=2026-02-01")).StatusCode);
    }

    [TestMethod]
    public async Task PayWithExpress_ChangeShipping_ThenCancelRefundsEverything()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var connectionId = await ConnectBankAsync(shopper);

        var started = await ReadAsync(await shopper.PostAsJsonAsync("/api/checkout/bank-payment", new
        {
            connectionId,
            items = new[] { new { catalogItemId = 1, quantity = 2 } },
            shippingCost = 5.00m
        }), HttpStatusCode.OK);
        var checkoutId = started["checkoutId"]!.GetValue<string>();
        var options = started["options"]!.AsArray().Select(o => o!.GetValue<string>()).ToList();
        CollectionAssert.AreEqual(new[] { "standard", "express" }, options);
        var oldTotal = started["total"]!.GetValue<decimal>();

        var changed = await ReadAsync(await shopper.PutAsJsonAsync(
            $"/api/checkout/bank-payment/{checkoutId}/shipping", new { shippingCost = 12.00m }), HttpStatusCode.OK);
        Assert.AreEqual(oldTotal + 7.00m, changed["total"]!.GetValue<decimal>());

        // somebody else cannot touch it
        var other = Client(ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await other.PostAsJsonAsync($"/api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" })).StatusCode);

        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await shopper.PostAsJsonAsync($"/api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "warp" })).StatusCode);

        var confirmed = await ReadAsync(await shopper.PostAsJsonAsync(
            $"/api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" }), HttpStatusCode.OK);
        var orderId = confirmed["orderId"]!.GetValue<int>();
        Assert.IsTrue(orderId > 0);
        Assert.IsTrue(confirmed["fee"]!.GetValue<decimal>() > 0);
        Assert.AreEqual(changed["total"]!.GetValue<decimal>() + confirmed["fee"]!.GetValue<decimal>(), confirmed["charged"]!.GetValue<decimal>());
        Assert.IsTrue(DateTime.TryParse(confirmed["arrivesOn"]!.GetValue<string>(), out _));

        // confirming again does not charge again
        var again = await ReadAsync(await shopper.PostAsJsonAsync(
            $"/api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" }), HttpStatusCode.OK);
        Assert.AreEqual(orderId, again["orderId"]!.GetValue<int>());

        // shipping can no longer change
        Assert.AreEqual(HttpStatusCode.Conflict,
            (await shopper.PutAsJsonAsync($"/api/checkout/bank-payment/{checkoutId}/shipping", new { shippingCost = 1m })).StatusCode);

        Assert.AreEqual(HttpStatusCode.NotFound, (await other.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);
        var cancelled = await ReadAsync(await shopper.PostAsync($"/api/orders/{orderId}/cancel", null), HttpStatusCode.OK);
        Assert.AreEqual(confirmed["charged"]!.GetValue<decimal>(), cancelled["refunded"]!.GetValue<decimal>());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);

        // disconnecting ends the connection
        Assert.AreEqual(HttpStatusCode.NoContent, (await shopper.DeleteAsync($"/api/bank/connections/{connectionId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.DeleteAsync($"/api/bank/connections/{connectionId}")).StatusCode);
    }

    [TestMethod]
    public async Task ShippedOrderCannotBeCancelled()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var connectionId = await ConnectBankAsync(shopper);
        var started = await ReadAsync(await shopper.PostAsJsonAsync("/api/checkout/bank-payment", new
        {
            connectionId,
            items = new[] { new { catalogItemId = 2, quantity = 1 } },
            shippingCost = 0m
        }), HttpStatusCode.OK);
        var checkoutId = started["checkoutId"]!.GetValue<string>();
        var confirmed = await ReadAsync(await shopper.PostAsJsonAsync(
            $"/api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "standard" }), HttpStatusCode.OK);
        Assert.AreEqual(0m, confirmed["fee"]!.GetValue<decimal>());
        var orderId = confirmed["orderId"]!.GetValue<int>();

        var admin = Client(ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/orders/{orderId}/ship", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);
    }

    [TestMethod]
    public async Task HugeOrdersAreRefusedWithAReason_NotAnError()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var connectionId = await ConnectBankAsync(shopper);
        var response = await shopper.PostAsJsonAsync("/api/checkout/bank-payment", new
        {
            connectionId,
            items = new[] { new { catalogItemId = 1, quantity = 100000000 } },
            shippingCost = 0m
        });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task SupportSeesSyncedActivityAndTransactionsBetweenDates()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        // A bank connected with auth only (before transactions were requested) must still be readable.
        await ConnectBankAsync(shopper, product: "auth");
        await ConnectBankAsync(shopper, institution: "ins_109509", product: "auth");

        var admin = Client(ApiTokenHelper.GetAdminUserToken());
        var activity = await ReadAsync(
            await admin.GetAsync("/api/support/shoppers/demouser@microsoft.com/bank-activity"), HttpStatusCode.OK);
        var transactions = activity["transactions"]!.AsArray();
        Assert.IsTrue(transactions.Count > 0);
        Assert.IsNotNull(transactions[0]!["name"]);
        Assert.IsNotNull(transactions[0]!["pending"]);

        // a second view is in step with the first (no duplicates)
        var second = await ReadAsync(
            await admin.GetAsync("/api/support/shoppers/demouser@microsoft.com/bank-activity"), HttpStatusCode.OK);
        Assert.AreEqual(transactions.Count, second["transactions"]!.AsArray().Count);

        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var from = DateTime.UtcNow.AddDays(-60).ToString("yyyy-MM-dd");
        var between = await ReadAsync(await admin.GetAsync(
            $"/api/support/shoppers/demouser@microsoft.com/transactions?from={from}&to={to}"), HttpStatusCode.OK);
        Assert.IsTrue(between["transactions"]!.AsArray().Count > 0);

        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.GetAsync(
            "/api/support/shoppers/demouser@microsoft.com/transactions?from=nope&to=2026-01-01")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.GetAsync(
            "/api/support/shoppers/demouser@microsoft.com/transactions?from=2026-02-01&to=2026-01-01")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.GetAsync(
            "/api/support/shoppers/nobody@example.com/bank-activity")).StatusCode);
    }

    [TestMethod]
    public async Task PortfolioShowsOptionsAsContracts()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.NotFound, (await Client(ApiTokenHelper.GetAdminUserToken()).GetAsync("/api/portfolio")).StatusCode);

        await ConnectBankAsync(shopper, type: "brokerage", institution: "ins_115616", product: "investments");
        var portfolio = await ReadAsync(await shopper.GetAsync("/api/portfolio"), HttpStatusCode.OK);
        var positions = portfolio["positions"]!.AsArray();
        Assert.IsTrue(positions.Count > 0);

        var option = positions.Single(p => p!["ticker"]?.GetValue<string>() == "NFLX180201C00355000")!;
        Assert.AreEqual(100m, option["contracts"]!.GetValue<decimal>());
        Assert.IsNull(option["quantity"]);

        var equity = positions.First(p => p!["ticker"]?.GetValue<string>() == "ACHN")!;
        Assert.AreEqual(1m, equity["quantity"]!.GetValue<decimal>());
        Assert.IsNull(equity["contracts"]);
    }
}
