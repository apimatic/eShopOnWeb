using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;
using ThePlaidApi.Servers;

namespace PublicApiIntegrationTests.PlaidEndpoints;

/// <summary>
/// End to end against the real Plaid sandbox (nothing is stubbed). The sandbox credentials come from the
/// PLAID_CLIENT_ID / PLAID_SECRET variables or the PublicApi user-secrets; without them the tests are inconclusive.
/// </summary>
[TestClass]
public class PayByBankTests
{
    private const string Platypus = "ins_109508";
    private static ThePlaidApiClient? _plaid;

    private static ThePlaidApiClient Plaid()
    {
        var id = Environment.GetEnvironmentVariable("PLAID_CLIENT_ID");
        var secret = Environment.GetEnvironmentVariable("PLAID_SECRET");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret))
            Assert.Inconclusive("PLAID_CLIENT_ID / PLAID_SECRET are not set; skipping the Plaid sandbox tests.");
        return _plaid ??= new ThePlaidApiClient(new HttpClient(), new ThePlaidApiClientOptions
        {
            Environment = ServerEnvironment.Environment2,
            ClientId = id,
            Secret = secret,
            PlaidVersion = "2020-09-14",
        });
    }

    /// <summary>What Plaid Link would hand the browser, produced by the sandbox.</summary>
    private static async Task<string> SandboxPublicToken(params Products[] products) =>
        (await Plaid().SandboxPublicTokenCreate(new SandboxPublicTokenCreateOperationRequest
        {
            Body = new SandboxPublicTokenCreateRequest { InstitutionId = Platypus, InitialProducts = products },
        })).PublicToken;

    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.Timeout = TimeSpan.FromMinutes(3);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient Shopper() => Client(ApiTokenHelper.GetNormalUserToken());
    private static HttpClient Admin() => Client(ApiTokenHelper.GetAdminUserToken());

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, response.StatusCode, text);
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<string> ConnectBank(HttpClient shopper)
    {
        var token = await SandboxPublicToken(Products.Transfer);
        var saved = await Json(await shopper.PostAsJsonAsync("api/bank/connections", new { publicToken = token }), HttpStatusCode.Created);
        return saved.GetProperty("connectionId").GetString()!;
    }

    private static async Task<JsonElement> Start(HttpClient shopper, string connectionId, int quantity, decimal shipping) =>
        await Json(await shopper.PostAsJsonAsync("api/checkout/bank-payment", new
        {
            connectionId,
            items = new[] { new { catalogItemId = 1, quantity } },
            shippingCost = shipping,
        }), HttpStatusCode.Created);

    [TestMethod]
    public async Task BankPickerListsAndSearchesBanksByName()
    {
        Plaid();
        var found = await Json(await Shopper().GetAsync("api/banks?query=first%20platypus"), HttpStatusCode.OK);
        var names = found.GetProperty("banks").EnumerateArray().Select(b => b.GetProperty("name").GetString()).ToList();
        CollectionAssert.Contains(names, "First Platypus Bank");
    }

    [TestMethod]
    public async Task ProtectedEndpointsRejectCallersWithoutTheRole()
    {
        var response = await Shopper().GetAsync("api/support/shoppers/demouser@microsoft.com/bank-activity");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);

        var anonymous = await ProgramTest.NewClient.GetAsync("api/portfolio");
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [TestMethod]
    public async Task ShopperPaysExpressChangesNothingAfterwardsAndCancelsForAFullRefund()
    {
        var shopper = Shopper();
        var connectionId = await ConnectBank(shopper);

        var started = await Start(shopper, connectionId, quantity: 2, shipping: 5m);
        CollectionAssert.AreEqual(new[] { "standard", "express" },
            started.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToArray());
        var checkoutId = started.GetProperty("checkoutId").GetString();
        var firstTotal = started.GetProperty("total").GetDecimal();

        // The shipping is recalculated before confirming: the new total is what gets charged.
        var changed = await Json(await shopper.PutAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/shipping", new { shippingCost = 8.5m }), HttpStatusCode.OK);
        Assert.AreEqual(firstTotal + 3.5m, changed.GetProperty("total").GetDecimal());

        var confirmed = await Json(await shopper.PostAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" }), HttpStatusCode.OK);
        Assert.AreEqual(3m, confirmed.GetProperty("fee").GetDecimal());
        Assert.AreEqual(changed.GetProperty("total").GetDecimal() + 3m, confirmed.GetProperty("total").GetDecimal());
        Assert.IsTrue(DateTime.TryParse(confirmed.GetProperty("arrivesOn").GetString(), out _));
        var orderId = confirmed.GetProperty("orderId").GetInt32();

        // Confirming twice never charges twice.
        var again = await Json(await shopper.PostAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" }), HttpStatusCode.OK);
        Assert.AreEqual(orderId, again.GetProperty("orderId").GetInt32());

        // Someone else can't cancel it; the owner gets everything back, fee included.
        Assert.AreEqual(HttpStatusCode.NotFound, (await Admin().PostAsync($"api/orders/{orderId}/cancel", null)).StatusCode);
        var cancelled = await Json(await shopper.PostAsync($"api/orders/{orderId}/cancel", null), HttpStatusCode.OK);
        Assert.AreEqual(confirmed.GetProperty("total").GetDecimal(), cancelled.GetProperty("refunded").GetDecimal());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/orders/{orderId}/cancel", null)).StatusCode);
    }

    [TestMethod]
    public async Task WholesaleOrdersAreOfferedStandardOnly()
    {
        var shopper = Shopper();
        var connectionId = await ConnectBank(shopper);

        var started = await Start(shopper, connectionId, quantity: 100_000, shipping: 50m);
        CollectionAssert.AreEqual(new[] { "standard" },
            started.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToArray());

        var checkoutId = started.GetProperty("checkoutId").GetString();
        var express = await shopper.PostAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, express.StatusCode);
    }

    [TestMethod]
    public async Task OneShoppersCheckoutAndConnectionAreInvisibleToAnother()
    {
        var shopper = Shopper();
        var connectionId = await ConnectBank(shopper);
        var admin = Admin();

        var started = await shopper.PostAsJsonAsync("api/checkout/bank-payment", new
        {
            connectionId,
            items = new[] { new { catalogItemId = 1, quantity = 1 } },
            shippingCost = 0,
        });
        Assert.AreEqual(HttpStatusCode.Created, started.StatusCode);

        var stolen = await admin.PostAsJsonAsync("api/checkout/bank-payment", new
        {
            connectionId,
            items = new[] { new { catalogItemId = 1, quantity = 1 } },
            shippingCost = 0,
        });
        Assert.AreEqual(HttpStatusCode.NotFound, stolen.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.DeleteAsync($"api/bank/connections/{connectionId}")).StatusCode);
    }

    [TestMethod]
    public async Task SupportSeesTheShoppersBankActivityAndDateRangeLookups()
    {
        var shopper = Shopper();
        await ConnectBank(shopper);
        var admin = Admin();

        var activity = await Json(await admin.GetAsync("api/support/shoppers/demouser@microsoft.com/bank-activity"), HttpStatusCode.OK);
        var rows = activity.GetProperty("transactions").EnumerateArray().ToList();
        Assert.IsTrue(rows.Count > 0);
        Assert.IsTrue(rows.All(r => r.TryGetProperty("name", out _) && r.TryGetProperty("amount", out _)
            && r.TryGetProperty("date", out _) && r.TryGetProperty("pending", out _)));

        var from = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var range = await Json(await admin.GetAsync($"api/support/shoppers/demouser@microsoft.com/transactions?from={from}&to={to}"), HttpStatusCode.OK);
        Assert.IsTrue(range.GetProperty("transactions").GetArrayLength() > 0);

        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await admin.GetAsync($"api/support/shoppers/demouser@microsoft.com/transactions?from={to}&to={from}")).StatusCode);
    }

    [TestMethod]
    public async Task PortfolioCountsOptionsInContractsAndEverythingElseInQuantity()
    {
        var shopper = Shopper();
        var token = await SandboxPublicToken(Products.Investments);
        await Json(await shopper.PostAsJsonAsync("api/bank/connections", new { publicToken = token, kind = "brokerage" }), HttpStatusCode.Created);

        var portfolio = await Json(await shopper.GetAsync("api/portfolio"), HttpStatusCode.OK);
        var positions = portfolio.GetProperty("positions").EnumerateArray().ToList();
        var option = positions.First(p => p.GetProperty("ticker").GetString() == "NFLX180201C00355000");
        Assert.AreEqual(100m, option.GetProperty("contracts").GetDecimal());
        Assert.IsFalse(option.TryGetProperty("quantity", out _));

        var equity = positions.First(p => p.GetProperty("ticker").GetString() == "SBSI");
        Assert.AreEqual(213m, equity.GetProperty("quantity").GetDecimal());
        Assert.IsFalse(equity.TryGetProperty("contracts", out _));
    }

    [TestMethod]
    public async Task DisconnectingForgetsTheConnection()
    {
        var shopper = Shopper();
        var connectionId = await ConnectBank(shopper);

        Assert.AreEqual(HttpStatusCode.NoContent, (await shopper.DeleteAsync($"api/bank/connections/{connectionId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.DeleteAsync($"api/bank/connections/{connectionId}")).StatusCode);

        var list = await Json(await shopper.GetAsync("api/bank/connections"), HttpStatusCode.OK);
        Assert.IsFalse(list.GetProperty("connections").EnumerateArray().Any(c => c.GetProperty("connectionId").GetString() == connectionId));
    }
}
