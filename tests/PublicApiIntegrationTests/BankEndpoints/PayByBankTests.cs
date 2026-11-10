using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace PublicApiIntegrationTests.BankEndpoints;

/// <summary>
/// Pay by bank, end to end against the real Plaid sandbox (credentials come from the Plaid: configuration
/// section, i.e. .NET user-secrets, or the PLAID_* environment variables). Nothing is stubbed.
/// </summary>
[TestClass]
public class PayByBankTests
{
    private static WebApplicationFactory<Program> _factory = null!;
    private static bool _plaidConfigured;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _factory = new WebApplicationFactory<Program>();
        var config = _factory.Services.GetRequiredService<IConfiguration>();
        _plaidConfigured =
            (!string.IsNullOrEmpty(config["Plaid:ClientId"]) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLAID_CLIENT_ID")))
            && (!string.IsNullOrEmpty(config["Plaid:Secret"]) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLAID_SECRET")));
    }

    [ClassCleanup]
    public static void ClassCleanup() => _factory.Dispose();

    private static HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient Shopper() => Client(ApiTokenHelper.GetNormalUserToken());
    private static HttpClient Admin() => Client(ApiTokenHelper.GetAdminUserToken());

    private static void RequirePlaid()
    {
        if (!_plaidConfigured) Assert.Inconclusive("Plaid sandbox credentials are not configured.");
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, response.StatusCode, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Creates a sandbox item the way Plaid Link would and returns its public token.</summary>
    private static async Task<string> SandboxPublicTokenAsync(string institutionId, Products product)
    {
        var plaid = _factory.Services.GetRequiredService<ThePlaidApiClient>();
        var response = await plaid.SandboxPublicTokenCreate(new SandboxPublicTokenCreateOperationRequest
        {
            Body = new SandboxPublicTokenCreateRequest { InstitutionId = institutionId, InitialProducts = new[] { product } },
        });
        return response.PublicToken;
    }

    private static async Task<int> ConnectBankAsync(HttpClient shopper, Products product)
    {
        var publicToken = await SandboxPublicTokenAsync("ins_109508", product);
        var created = await Json(await shopper.PostAsJsonAsync("api/bank/connections", new { publicToken }), HttpStatusCode.Created);
        return created.GetProperty("connectionId").GetInt32();
    }

    [TestMethod]
    public async Task EndpointsRequireAToken()
    {
        var anonymous = _factory.CreateClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/banks")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/portfolio")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("api/bank/link-token", new { })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("api/orders/1/cancel", null)).StatusCode);
    }

    [TestMethod]
    public async Task SupportEndpointsAreForAdministratorsOnly()
    {
        var shopper = Shopper();
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync("api/support/shoppers/demouser@microsoft.com/bank-activity")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.GetAsync("api/support/shoppers/demouser@microsoft.com/transactions?from=2026-01-01&to=2026-01-31")).StatusCode);
    }

    [TestMethod]
    public async Task TransactionsLookupValidatesTheDates()
    {
        var admin = Admin();
        foreach (var query in new[] { "", "?from=2026-01-01", "?from=2026-13-01&to=2026-01-31", "?from=2026-02-01&to=2026-01-01", "?from=yesterday&to=today" })
        {
            var response = await admin.GetAsync($"api/support/shoppers/demouser@microsoft.com/transactions{query}");
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, query);
        }
    }

    [TestMethod]
    public async Task BankPickerListsAndSearchesBanks()
    {
        RequirePlaid();
        var body = await Json(await Shopper().GetAsync("api/banks?query=chase"), HttpStatusCode.OK);
        var names = body.GetProperty("banks").EnumerateArray().Select(b => b.GetProperty("name").GetString()!).ToList();
        Assert.IsTrue(names.Count > 0);
        Assert.IsTrue(names.All(n => n.Contains("chase", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task LinkTokenIsCreated()
    {
        RequirePlaid();
        var body = await Json(await Shopper().PostAsJsonAsync("api/bank/link-token", new { }), HttpStatusCode.OK);
        StringAssert.StartsWith(body.GetProperty("linkToken").GetString(), "link-");
    }

    [TestMethod]
    public async Task ABadPublicTokenIsRefusedWith4xx()
    {
        RequirePlaid();
        var response = await Shopper().PostAsJsonAsync("api/bank/connections", new { publicToken = "public-sandbox-not-a-token" });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "reason");
    }

    [TestMethod]
    public async Task PayCancelAndDisconnect()
    {
        RequirePlaid();
        var shopper = Shopper();
        var connectionId = await ConnectBankAsync(shopper, Products.Transfer);

        // Start: the total is priced from the catalog, express is offered below the same-day limit.
        var started = await Json(await shopper.PostAsJsonAsync("api/checkout/bank-payment", new
        {
            connectionId,
            items = new[] { new { catalogItemId = 1, quantity = 2 } },
            shippingCost = 5.50m,
        }), HttpStatusCode.OK);
        var checkoutId = started.GetProperty("checkoutId").GetString();
        var options = started.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToList();
        CollectionAssert.AreEquivalent(new[] { "standard", "express" }, options);
        var subtotal = started.GetProperty("subtotal").GetDecimal();
        Assert.AreEqual(subtotal + 5.50m, started.GetProperty("total").GetDecimal());

        // The shipping changes before confirming: the new total is what is charged.
        var repriced = await Json(await shopper.PutAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/shipping", new { shippingCost = 9.25m }), HttpStatusCode.OK);
        Assert.AreEqual(subtotal + 9.25m, repriced.GetProperty("total").GetDecimal());

        var confirmed = await Json(await shopper.PostAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" }), HttpStatusCode.OK);
        Assert.IsTrue(confirmed.GetProperty("fee").GetDecimal() > 0);
        Assert.AreEqual(subtotal + 9.25m + confirmed.GetProperty("fee").GetDecimal(), confirmed.GetProperty("totalCharged").GetDecimal());
        Assert.IsTrue(DateOnly.TryParse(confirmed.GetProperty("arrivesOn").GetString(), out _));
        var orderId = confirmed.GetProperty("orderId").GetInt32();

        // Confirming again must not charge twice; it answers with the same order.
        var again = await Json(await shopper.PostAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" }), HttpStatusCode.OK);
        Assert.AreEqual(orderId, again.GetProperty("orderId").GetInt32());

        // The shipping can't change after confirming.
        Assert.AreEqual(HttpStatusCode.Conflict,
            (await shopper.PutAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/shipping", new { shippingCost = 1m })).StatusCode);

        // Someone else's order is not found; the owner can cancel, once.
        Assert.AreEqual(HttpStatusCode.NotFound, (await Admin().PostAsync($"api/orders/{orderId}/cancel", null)).StatusCode);
        await Json(await shopper.PostAsync($"api/orders/{orderId}/cancel", null), HttpStatusCode.OK);
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/orders/{orderId}/cancel", null)).StatusCode);

        // Disconnect: gone for good.
        await Json(await shopper.DeleteAsync($"api/bank/connections/{connectionId}"), HttpStatusCode.OK);
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.DeleteAsync($"api/bank/connections/{connectionId}")).StatusCode);
    }

    [TestMethod]
    public async Task WholesaleOrdersAreNotOfferedExpress()
    {
        RequirePlaid();
        var shopper = Shopper();
        var connectionId = await ConnectBankAsync(shopper, Products.Transfer);
        try
        {
            var started = await Json(await shopper.PostAsJsonAsync("api/checkout/bank-payment", new
            {
                connectionId,
                items = new[] { new { catalogItemId = 1, quantity = 100_000 } },
                shippingCost = 0m,
            }), HttpStatusCode.OK);
            var options = started.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToList();
            CollectionAssert.AreEqual(new[] { "standard" }, options);

            var checkoutId = started.GetProperty("checkoutId").GetString();
            var express = await shopper.PostAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" });
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, express.StatusCode);
        }
        finally
        {
            await shopper.DeleteAsync($"api/bank/connections/{connectionId}");
        }
    }

    [TestMethod]
    public async Task SupportSeesActivityAndDatedTransactions()
    {
        RequirePlaid();
        var shopper = Shopper();
        // Connected without the transactions product: the lookups must still work.
        var connectionId = await ConnectBankAsync(shopper, Products.Transfer);
        try
        {
            var admin = Admin();
            var activity = await Json(await admin.GetAsync("api/support/shoppers/demouser@microsoft.com/bank-activity"), HttpStatusCode.OK);
            var count = activity.GetProperty("transactions").GetArrayLength();
            Assert.IsTrue(count > 0);
            var first = activity.GetProperty("transactions")[0];
            Assert.IsTrue(first.TryGetProperty("name", out _) && first.TryGetProperty("pending", out _) && first.TryGetProperty("amount", out _));

            // Syncing again must not duplicate anything.
            var second = await Json(await admin.GetAsync("api/support/shoppers/demouser@microsoft.com/bank-activity"), HttpStatusCode.OK);
            Assert.AreEqual(count, second.GetProperty("transactions").GetArrayLength());

            var from = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd");
            var to = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var range = await Json(await admin.GetAsync($"api/support/shoppers/demouser@microsoft.com/transactions?from={from}&to={to}"), HttpStatusCode.OK);
            foreach (var t in range.GetProperty("transactions").EnumerateArray())
            {
                var date = t.GetProperty("date").GetString()!;
                Assert.IsTrue(string.CompareOrdinal(date, from) >= 0 && string.CompareOrdinal(date, to) <= 0, date);
            }

            Assert.AreEqual(HttpStatusCode.NotFound, (await admin.GetAsync("api/support/shoppers/nobody@example.com/bank-activity")).StatusCode);
        }
        finally
        {
            await shopper.DeleteAsync($"api/bank/connections/{connectionId}");
        }
    }

    [TestMethod]
    public async Task PortfolioShowsOptionsInContracts()
    {
        RequirePlaid();
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var publicToken = await SandboxPublicTokenAsync("ins_115616", Products.Investments);
        var created = await Json(await shopper.PostAsJsonAsync("api/bank/connections", new { publicToken, purpose = "brokerage" }), HttpStatusCode.Created);
        var connectionId = created.GetProperty("connectionId").GetInt32();
        try
        {
            var portfolio = await Json(await shopper.GetAsync("api/portfolio"), HttpStatusCode.OK);
            var positions = portfolio.GetProperty("positions").EnumerateArray().ToList();
            Assert.IsTrue(positions.Count > 0);

            var option = positions.First(p => p.GetProperty("type").GetString() == "derivative");
            Assert.IsTrue(option.TryGetProperty("contracts", out var contracts) && contracts.GetDecimal() > 0);
            Assert.IsFalse(option.TryGetProperty("quantity", out _));

            var equity = positions.First(p => p.GetProperty("type").GetString() == "equity");
            Assert.IsTrue(equity.TryGetProperty("quantity", out _));
            Assert.IsFalse(equity.TryGetProperty("contracts", out _));
        }
        finally
        {
            await shopper.DeleteAsync($"api/bank/connections/{connectionId}");
        }
    }
}
