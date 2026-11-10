using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.BankEndpoints;

/// <summary>
/// These talk to the real Plaid sandbox. The app reads its credentials from the Plaid: settings (user-secrets);
/// the test itself needs PLAID_CLIENT_ID and PLAID_SECRET to create sandbox bank logins.
/// </summary>
[TestClass]
public class PayByBankTests
{
    private static readonly HttpClient SandboxPlaid = new() { BaseAddress = new Uri("https://sandbox.plaid.com") };

    private static HttpClient As(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient Shopper() => As(ApiTokenHelper.GetNormalUserToken());
    private static HttpClient Admin() => As(ApiTokenHelper.GetAdminUserToken());

    private static async Task<string> SandboxPublicToken(string institution, params string[] products)
    {
        var id = Environment.GetEnvironmentVariable("PLAID_CLIENT_ID");
        var secret = Environment.GetEnvironmentVariable("PLAID_SECRET");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret))
        {
            Assert.Inconclusive("PLAID_CLIENT_ID / PLAID_SECRET are not set.");
        }

        var response = await SandboxPlaid.PostAsJsonAsync("/sandbox/public_token/create", new
        {
            client_id = id,
            secret,
            institution_id = institution,
            initial_products = products,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("public_token").GetString()!;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<int> ConnectBank(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("api/bank/connections", new { publicToken = await SandboxPublicToken("ins_109508", "auth", "transactions") });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("connection").GetProperty("id").GetInt32();
    }

    private static async Task<(Guid CheckoutId, JsonElement Body)> Start(HttpClient client, int connectionId, int quantity, decimal shipping)
    {
        var response = await client.PostAsJsonAsync("api/checkout/bank-payment", new
        {
            connectionId,
            items = new[] { new { catalogItemId = 1, quantity } },
            shippingCost = shipping,
        });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        return (body.GetProperty("checkoutId").GetGuid(), body);
    }

    [TestMethod]
    public async Task ShopperEndpointsNeedAToken_AndSupportEndpointsNeedTheAdministratorRole()
    {
        var anonymous = ProgramTest.NewClient;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/banks")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/portfolio")).StatusCode);

        var shopper = Shopper();
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync("api/support/shoppers/demouser@microsoft.com/bank-activity")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync("api/support/shoppers/demouser@microsoft.com/transactions?from=2026-01-01&to=2026-01-31")).StatusCode);
    }

    [TestMethod]
    public async Task BankPickerListsAndSearchesBanks()
    {
        var shopper = Shopper();
        var list = await Json(await shopper.GetAsync("api/banks?count=5"));
        Assert.AreEqual(5, list.GetProperty("banks").GetArrayLength());

        var search = await Json(await shopper.GetAsync("api/banks?query=chase"));
        Assert.IsTrue(search.GetProperty("banks").EnumerateArray().Any(b => b.GetProperty("name").GetString() == "Chase"));

        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync("api/banks?count=100000")).StatusCode);
    }

    [TestMethod]
    public async Task PaymentLifecycle_StartChangeShippingConfirmAndCancel()
    {
        var shopper = Shopper();
        var connection = await ConnectBank(shopper);

        var (checkoutId, started) = await Start(shopper, connection, 1, 5m);
        Assert.AreEqual(24.5m, started.GetProperty("total").GetDecimal()); // catalog item 1 costs 19.50
        CollectionAssert.AreEqual(new[] { "standard", "express" }, started.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToArray());

        // a new shipping cost changes what confirming charges
        var changed = await shopper.PutAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/shipping", new { shippingCost = 7.25m });
        Assert.AreEqual(26.75m, (await Json(changed)).GetProperty("total").GetDecimal());

        var confirmed = await shopper.PostAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" });
        Assert.AreEqual(HttpStatusCode.OK, confirmed.StatusCode, await confirmed.Content.ReadAsStringAsync());
        var confirmation = await Json(confirmed);
        Assert.AreEqual(2.5m, confirmation.GetProperty("fee").GetDecimal());
        Assert.AreEqual(29.25m, confirmation.GetProperty("charged").GetDecimal());
        Assert.IsTrue(DateOnly.TryParse(confirmation.GetProperty("arrivesOn").GetString(), out _));
        var orderId = confirmation.GetProperty("orderId").GetInt32();

        // confirming again does not charge again
        var again = await shopper.PostAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" });
        Assert.AreEqual(orderId, (await Json(again)).GetProperty("orderId").GetInt32());

        // somebody else's order is not theirs to cancel
        Assert.AreEqual(HttpStatusCode.NotFound, (await Admin().PostAsync($"api/orders/{orderId}/cancel", null)).StatusCode);

        var cancelled = await shopper.PostAsync($"api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancelled.StatusCode, await cancelled.Content.ReadAsStringAsync());
        Assert.AreEqual(29.25m, (await Json(cancelled)).GetProperty("refunded").GetDecimal());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/orders/{orderId}/cancel", null)).StatusCode);
    }

    [TestMethod]
    public async Task VeryLargeOrdersCanBePaidButNotExpress()
    {
        var shopper = Shopper();
        var connection = await ConnectBank(shopper);

        var (checkoutId, started) = await Start(shopper, connection, 100_000, 0m);
        CollectionAssert.AreEqual(new[] { "standard" }, started.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToArray());
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity,
            (await shopper.PostAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" })).StatusCode);
    }

    [TestMethod]
    public async Task ConnectionsBelongToTheirShopper_AndDisconnectingRemovesThem()
    {
        var shopper = Shopper();
        var connection = await ConnectBank(shopper);

        var response = await Admin().PostAsJsonAsync("api/checkout/bank-payment", new
        {
            connectionId = connection,
            items = new[] { new { catalogItemId = 1, quantity = 1 } },
            shippingCost = 0m,
        });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);

        Assert.AreEqual(HttpStatusCode.NotFound, (await Admin().DeleteAsync($"api/bank/connections/{connection}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, (await shopper.DeleteAsync($"api/bank/connections/{connection}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.DeleteAsync($"api/bank/connections/{connection}")).StatusCode);
    }

    [TestMethod]
    public async Task SupportSeesBankActivityAndTransactionsBetweenDates()
    {
        var shopper = Shopper();
        await ConnectBank(shopper);
        var admin = Admin();

        var activity = await Json(await admin.GetAsync("api/support/shoppers/demouser@microsoft.com/bank-activity"));
        var first = activity.GetProperty("transactions").EnumerateArray().First();
        Assert.IsTrue(first.TryGetProperty("name", out _) && first.TryGetProperty("pending", out _));

        var today = DateTime.UtcNow.Date;
        var range = await admin.GetAsync($"api/support/shoppers/demouser@microsoft.com/transactions?from={today.AddDays(-30):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}");
        Assert.AreEqual(HttpStatusCode.OK, range.StatusCode, await range.Content.ReadAsStringAsync());
        Assert.IsTrue((await Json(range)).GetProperty("transactions").GetArrayLength() > 0);

        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await admin.GetAsync("api/support/shoppers/demouser@microsoft.com/transactions?from=2026-02-01&to=2026-01-01")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.GetAsync("api/support/shoppers/nobody@example.com/bank-activity")).StatusCode);
    }

    [TestMethod]
    public async Task PortfolioListsHoldings_OptionsInContracts()
    {
        var shopper = Shopper();
        var response = await shopper.PostAsJsonAsync("api/bank/connections", new { type = "brokerage", publicToken = await SandboxPublicToken("ins_115616", "investments") });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());

        var portfolio = await Json(await shopper.GetAsync("api/portfolio"));
        var positions = portfolio.GetProperty("positions").EnumerateArray().ToList();
        Assert.IsTrue(positions.Count > 0);

        var option = positions.First(p => p.GetProperty("ticker").GetString() == "NFLX180201C00355000");
        Assert.AreEqual(100m, option.GetProperty("contracts").GetDecimal()); // 10000 units of 100 per contract
        Assert.IsFalse(option.TryGetProperty("quantity", out _));

        var equity = positions.First(p => p.GetProperty("ticker").GetString() == "ACHN");
        Assert.AreEqual(1m, equity.GetProperty("quantity").GetDecimal());
        Assert.IsFalse(equity.TryGetProperty("contracts", out _));
    }
}
