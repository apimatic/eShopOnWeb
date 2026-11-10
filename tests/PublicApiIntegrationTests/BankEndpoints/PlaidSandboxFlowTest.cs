using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BankEndpoints;

/// <summary>
/// Runs the pay-by-bank features against the real Plaid sandbox. The sandbox credentials come from the
/// PLAID_CLIENT_ID / PLAID_SECRET / PLAID_ENV environment variables; nothing is faked.
/// </summary>
[TestClass]
public class PlaidSandboxFlowTest
{
    private static readonly HttpClient Plaid = new() { BaseAddress = new Uri("https://sandbox.plaid.com/") };

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        var id = Environment.GetEnvironmentVariable("PLAID_CLIENT_ID");
        var secret = Environment.GetEnvironmentVariable("PLAID_SECRET");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret)) Assert.Inconclusive("Plaid sandbox credentials not set.");
        Environment.SetEnvironmentVariable("Plaid__ClientId", id);
        Environment.SetEnvironmentVariable("Plaid__Secret", secret);
        Environment.SetEnvironmentVariable("Plaid__Environment", Environment.GetEnvironmentVariable("PLAID_ENV") ?? "sandbox");
    }

    private static async Task<string> SandboxPublicTokenAsync(string institution, params string[] products)
    {
        var res = await Plaid.PostAsJsonAsync("sandbox/public_token/create", new
        {
            client_id = Environment.GetEnvironmentVariable("PLAID_CLIENT_ID"),
            secret = Environment.GetEnvironmentVariable("PLAID_SECRET"),
            institution_id = institution,
            initial_products = products,
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonNode>())!["public_token"]!.GetValue<string>();
    }

    private static HttpClient ClientFor(string user, params string[] roles)
    {
        var c = ProgramTest.NewClient;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.CreateToken(user, roles));
        return c;
    }

    private static async Task<JsonNode> Json(HttpResponseMessage r, HttpStatusCode expected)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, r.StatusCode, text);
        return JsonNode.Parse(text)!;
    }

    [TestMethod]
    public async Task PayByBankLifecycle()
    {
        var shopper = ClientFor("demouser@microsoft.com");
        var admin = ClientFor("admin@microsoft.com", "Administrators");

        // 1. bank picker
        var banks = await Json(await shopper.GetAsync("api/banks?query=platypus"), HttpStatusCode.OK);
        Assert.IsTrue(banks["banks"]!.AsArray().Count > 0);

        // 2. connect
        var link = await Json(await shopper.PostAsJsonAsync("api/bank/link-token", new { type = "bank" }), HttpStatusCode.OK);
        Assert.IsTrue(link["linkToken"]!.GetValue<string>().StartsWith("link-"));
        var pt = await SandboxPublicTokenAsync("ins_109508", "auth", "transactions");
        var conn = await Json(await shopper.PostAsJsonAsync("api/bank/connections", new { publicToken = pt }), HttpStatusCode.Created);
        var connectionId = conn["connectionId"]!.GetValue<int>();

        // 4. start
        var start = await Json(await shopper.PostAsJsonAsync("api/checkout/bank-payment", new
        {
            connectionId,
            items = new[] { new { catalogItemId = 1, quantity = 2 } },
            shippingCost = 5.00m,
        }), HttpStatusCode.OK);
        var checkoutId = start["checkoutId"]!.GetValue<string>();
        Assert.IsTrue(start["options"]!.AsArray().Any(o => o!.GetValue<string>() == "standard"));

        // 5. shipping changes
        var changed = await Json(await shopper.PutAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/shipping", new { shippingCost = 9.50m }), HttpStatusCode.OK);
        Assert.AreEqual(start["total"]!.GetValue<decimal>() + 4.50m, changed["total"]!.GetValue<decimal>());

        // 6. confirm (twice: no double charge)
        var confirm = await Json(await shopper.PostAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "standard" }), HttpStatusCode.OK);
        var again = await Json(await shopper.PostAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "standard" }), HttpStatusCode.OK);
        Assert.AreEqual(confirm["orderId"]!.GetValue<int>(), again["orderId"]!.GetValue<int>());
        Assert.AreEqual(0m, confirm["fee"]!.GetValue<decimal>());
        Assert.IsNotNull(confirm["arrivesOn"]);
        var orderId = confirm["orderId"]!.GetValue<int>();

        // someone else can't cancel it
        var other = ClientFor("someone@else.com");
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.PostAsync($"api/orders/{orderId}/cancel", null)).StatusCode);

        // 7. cancel
        await Json(await shopper.PostAsync($"api/orders/{orderId}/cancel", null), HttpStatusCode.OK);

        // 8/9. support views
        var activity = await Json(await admin.GetAsync("api/support/shoppers/DemoUser@microsoft.com/bank-activity"), HttpStatusCode.OK);
        Assert.IsNotNull(activity["transactions"]);
        var range = await Json(await admin.GetAsync("api/support/shoppers/demouser@microsoft.com/transactions?from=2026-01-01&to=2026-11-01"), HttpStatusCode.OK);
        Assert.IsNotNull(range["transactions"]);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync("api/support/shoppers/demouser@microsoft.com/bank-activity")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.GetAsync("api/support/shoppers/demouser@microsoft.com/transactions?from=2026-02-01&to=2026-01-01")).StatusCode);

        // 3. disconnect
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.DeleteAsync($"api/bank/connections/{connectionId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, (await shopper.DeleteAsync($"api/bank/connections/{connectionId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.DeleteAsync($"api/bank/connections/{connectionId}")).StatusCode);
    }

    [TestMethod]
    public async Task PortfolioShowsHoldings()
    {
        var shopper = ClientFor("portfolio@microsoft.com");
        var pt = await SandboxPublicTokenAsync("ins_115616", "investments");
        var conn = await Json(await shopper.PostAsJsonAsync("api/bank/connections", new { publicToken = pt }), HttpStatusCode.Created);
        var portfolio = await Json(await shopper.GetAsync("api/portfolio"), HttpStatusCode.OK);
        Console.WriteLine(portfolio.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Assert.IsTrue(portfolio["positions"]!.AsArray().Count > 0);
        await shopper.DeleteAsync($"api/bank/connections/{conn["connectionId"]!.GetValue<int>()}");
    }
}
