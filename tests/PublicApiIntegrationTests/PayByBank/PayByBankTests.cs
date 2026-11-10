using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;
using ThePlaidApi.Servers;

namespace PublicApiIntegrationTests.PayByBank;

/// <summary>
/// End-to-end tests against the real Plaid sandbox (credentials come from the PLAID_* environment
/// variables / Plaid: user-secrets; nothing is stubbed).
/// </summary>
[TestClass]
public class PayByBankTests
{
    private const string PlatypusBank = "ins_109508"; // First Platypus Bank
    private const string CustomBalanceBank = "{\"override_accounts\":[{\"type\":\"depository\",\"subtype\":\"checking\",\"starting_balance\":50000000,\"meta\":{\"name\":\"Big Checking\"}}]}";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static ThePlaidApiClient SandboxClient()
    {
        var id = Environment.GetEnvironmentVariable("PLAID_CLIENT_ID");
        var secret = Environment.GetEnvironmentVariable("PLAID_SECRET");
        Assert.IsFalse(string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret), "PLAID_CLIENT_ID / PLAID_SECRET must be set");
        return new ThePlaidApiClient(new HttpClient(), new ThePlaidApiClientOptions
        {
            Environment = ServerEnvironment.Environment2,
            ClientId = id,
            Secret = secret,
            PlaidVersion = "2020-09-14"
        });
    }

    private static async Task<string> NewPublicTokenAsync(Products[] products, string? customPassword = null)
    {
        var response = await SandboxClient().SandboxPublicTokenCreate(new SandboxPublicTokenCreateOperationRequest
        {
            Body = new SandboxPublicTokenCreateRequest
            {
                InstitutionId = PlatypusBank,
                InitialProducts = products,
                Options = customPassword == null
                    ? null
                    : new SandboxPublicTokenCreateRequestOptions { OverrideUsername = "user_custom", OverridePassword = customPassword }
            }
        });
        return response.PublicToken;
    }

    private static HttpClient ClientFor(string? user, params string[] roles)
    {
        var client = ProgramTest.NewClient;
        if (user != null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetUserToken(user, roles));
        return client;
    }

    private static string NewShopper() => $"shopper-{Guid.NewGuid():N}@example.com";

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, response.StatusCode, text);
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<int> ConnectAsync(HttpClient shopper, Products[] products, string kind, string? customPassword = null)
    {
        var publicToken = await NewPublicTokenAsync(products, customPassword);
        var body = await ReadAsync(await shopper.PostAsJsonAsync("/api/bank/connections", new { publicToken, kind }), HttpStatusCode.Created);
        return body.GetProperty("connectionId").GetInt32();
    }

    // ------------------------------------------------------------------ 1

    [TestMethod]
    public async Task BankPicker_ListsAndSearchesBanksByName()
    {
        var shopper = ClientFor(NewShopper());

        var all = await ReadAsync(await shopper.GetAsync("/api/banks"), HttpStatusCode.OK);
        Assert.IsTrue(all.GetProperty("banks").GetArrayLength() > 0);

        var found = await ReadAsync(await shopper.GetAsync("/api/banks?query=Platypus"), HttpStatusCode.OK);
        var names = found.GetProperty("banks").EnumerateArray().Select(b => b.GetProperty("name").GetString()).ToList();
        Assert.IsTrue(names.Any(n => n!.Contains("Platypus", StringComparison.OrdinalIgnoreCase)), string.Join(", ", names));

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await ProgramTest.NewClient.GetAsync("/api/banks")).StatusCode);
    }

    [TestMethod]
    public async Task PlaidBaseUrl_WhenSet_IsUsedVerbatimInsteadOfTheEnvironmentDefault()
    {
        // Nothing listens on this address, so the call can only fail if the override is honoured.
        var app = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("Plaid:BaseUrl", "http://127.0.0.1:1"));
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetUserToken(NewShopper()));
        var response = await client.GetAsync("/api/banks");
        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task LinkToken_IsIssuedForTheSignedInShopper()
    {
        var shopper = ClientFor(NewShopper());
        var bank = await ReadAsync(await shopper.PostAsync("/api/bank/link-token", null), HttpStatusCode.OK);
        Assert.IsTrue(bank.GetProperty("linkToken").GetString()!.StartsWith("link-"));
        var brokerage = await ReadAsync(await shopper.PostAsJsonAsync("/api/bank/link-token", new { kind = "brokerage" }), HttpStatusCode.OK);
        Assert.IsTrue(brokerage.GetProperty("linkToken").GetString()!.StartsWith("link-"));
    }

    // ------------------------------------------------------- 2-7 payment

    [TestMethod]
    public async Task Payment_StartChangeShippingConfirmAndCancel()
    {
        var email = NewShopper();
        var shopper = ClientFor(email);
        var connectionId = await ConnectAsync(shopper, [Products.Auth, Products.Transactions], "bank");

        // Another shopper cannot use this connection.
        var other = ClientFor(NewShopper());
        var stolen = await other.PostAsJsonAsync("/api/checkout/bank-payment", new { connectionId, items = new[] { new { catalogItemId = 2, quantity = 1 } }, shippingCost = 1.00m });
        Assert.AreEqual(HttpStatusCode.NotFound, stolen.StatusCode);

        var started = await ReadAsync(await shopper.PostAsJsonAsync("/api/checkout/bank-payment",
            new { connectionId, items = new[] { new { catalogItemId = 2, quantity = 1 } }, shippingCost = 2.00m }), HttpStatusCode.Created);
        var checkoutId = started.GetProperty("checkoutId").GetGuid();
        var options = started.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToList();
        CollectionAssert.Contains(options, "standard");
        var subtotal = started.GetProperty("subtotal").GetDecimal();
        Assert.AreEqual(subtotal + 2.00m, started.GetProperty("total").GetDecimal());

        // Shipping is recalculated after the payment was started.
        var changed = await ReadAsync(await shopper.PutAsJsonAsync($"/api/checkout/bank-payment/{checkoutId}/shipping", new { shippingCost = 3.50m }), HttpStatusCode.OK);
        Assert.AreEqual(subtotal + 3.50m, changed.GetProperty("total").GetDecimal());

        var badSpeed = await shopper.PostAsJsonAsync($"/api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "teleport" });
        Assert.AreEqual(HttpStatusCode.BadRequest, badSpeed.StatusCode);

        var speed = options.Contains("express") ? "express" : "standard";
        var confirmed = await ReadAsync(await shopper.PostAsJsonAsync($"/api/checkout/bank-payment/{checkoutId}/confirm", new { speed }), HttpStatusCode.OK);
        var orderId = confirmed.GetProperty("orderId").GetInt32();
        var fee = confirmed.GetProperty("fee").GetDecimal();
        Assert.AreEqual(speed == "express", fee > 0m);
        Assert.AreEqual(JsonValueKind.String, confirmed.GetProperty("arrivesOn").ValueKind);
        Assert.IsTrue(DateOnly.TryParse(confirmed.GetProperty("arrivesOn").GetString(), out _));

        // The new total is what was charged at Plaid.
        using (var scope = ProgramTest.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
            var payment = await db.BankPayments.Include(p => p.Transfers).SingleAsync(p => p.OrderId == orderId);
            Assert.AreEqual(subtotal + 3.50m + fee, payment.Charged);
            var transfer = await SandboxClient().TransferGet(new TransferGetOperationRequest { Body = new TransferGetRequest { TransferId = payment.Transfers.Single().PlaidTransferId } });
            Assert.AreEqual(payment.Charged, decimal.Parse(transfer.Transfer.Amount, System.Globalization.CultureInfo.InvariantCulture));
        }

        // Confirming again must not charge twice.
        var again = await ReadAsync(await shopper.PostAsJsonAsync($"/api/checkout/bank-payment/{checkoutId}/confirm", new { speed }), HttpStatusCode.OK);
        Assert.AreEqual(orderId, again.GetProperty("orderId").GetInt32());

        // Someone else's order cannot be cancelled; the owner's can, and gets everything back.
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);
        var cancelled = await ReadAsync(await shopper.PostAsync($"/api/orders/{orderId}/cancel", null), HttpStatusCode.OK);
        Assert.AreEqual(subtotal + 3.50m + fee, cancelled.GetProperty("refunded").GetDecimal());

        using (var scope = ProgramTest.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
            var payment = await db.BankPayments.Include(p => p.Transfers).SingleAsync(p => p.OrderId == orderId);
            var transfer = (await SandboxClient().TransferGet(new TransferGetOperationRequest { Body = new TransferGetRequest { TransferId = payment.Transfers.Single().PlaidTransferId } })).Transfer;
            Assert.IsTrue(transfer.Status == TransferStatus.Cancelled || transfer.Refunds.Count > 0, "transfer should be cancelled or refunded");
        }
    }

    [TestMethod]
    public async Task Payment_ShippedOrderCannotBeCancelled()
    {
        var shopper = ClientFor(NewShopper());
        var connectionId = await ConnectAsync(shopper, [Products.Auth], "bank");
        var started = await ReadAsync(await shopper.PostAsJsonAsync("/api/checkout/bank-payment",
            new { connectionId, items = new[] { new { catalogItemId = 3, quantity = 1 } }, shippingCost = 0m }), HttpStatusCode.Created);
        var checkoutId = started.GetProperty("checkoutId").GetGuid();
        var confirmed = await ReadAsync(await shopper.PostAsJsonAsync($"/api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "standard" }), HttpStatusCode.OK);
        var orderId = confirmed.GetProperty("orderId").GetInt32();

        using (var scope = ProgramTest.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
            var order = await db.Orders.SingleAsync(o => o.Id == orderId);
            order.MarkShipped();
            await db.SaveChangesAsync();
        }

        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);
    }

    [TestMethod]
    public async Task Payment_InvalidRequestsAreRejectedWith4xx()
    {
        var shopper = ClientFor(NewShopper());
        var connectionId = await ConnectAsync(shopper, [Products.Auth], "bank");

        var noItems = await shopper.PostAsJsonAsync("/api/checkout/bank-payment", new { connectionId, items = Array.Empty<object>(), shippingCost = 1m });
        Assert.AreEqual(HttpStatusCode.BadRequest, noItems.StatusCode);
        var unknown = await shopper.PostAsJsonAsync("/api/checkout/bank-payment", new { connectionId, items = new[] { new { catalogItemId = 999999, quantity = 1 } }, shippingCost = 1m });
        Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);
        var negative = await shopper.PostAsJsonAsync("/api/checkout/bank-payment", new { connectionId, items = new[] { new { catalogItemId = 2, quantity = 1 } }, shippingCost = -1m });
        Assert.AreEqual(HttpStatusCode.BadRequest, negative.StatusCode);
        var missingConnection = await shopper.PostAsJsonAsync("/api/checkout/bank-payment", new { connectionId = 424242, items = new[] { new { catalogItemId = 2, quantity = 1 } }, shippingCost = 1m });
        Assert.AreEqual(HttpStatusCode.NotFound, missingConnection.StatusCode);
        var missingCheckout = await shopper.PutAsJsonAsync($"/api/checkout/bank-payment/{Guid.NewGuid()}/shipping", new { shippingCost = 1m });
        Assert.AreEqual(HttpStatusCode.NotFound, missingCheckout.StatusCode);
    }

    [TestMethod]
    public async Task Payment_WholesaleOrderAboveOneMillion_HasNoExpressAndIsSplitAcrossTransfers()
    {
        var shopper = ClientFor(NewShopper());
        var connectionId = await ConnectAsync(shopper, [Products.Auth], "bank", CustomBalanceBank);

        // A wholesale-sized order (well above the $1,000,000 same-day limit and the $10,000,000 single-transfer limit).
        var started = await ReadAsync(await shopper.PostAsJsonAsync("/api/checkout/bank-payment",
            new { connectionId, items = new[] { new { catalogItemId = 2, quantity = 2000000 } }, shippingCost = 250.00m }), HttpStatusCode.Created);
        var checkoutId = started.GetProperty("checkoutId").GetGuid();
        var total = started.GetProperty("total").GetDecimal();
        Assert.IsTrue(total > 10_000_000m, total.ToString());
        var options = started.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToList();
        CollectionAssert.AreEqual(new[] { "standard" }, options);

        var express = await shopper.PostAsJsonAsync($"/api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" });
        Assert.AreEqual(HttpStatusCode.Conflict, express.StatusCode);

        var confirmed = await ReadAsync(await shopper.PostAsJsonAsync($"/api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "standard" }), HttpStatusCode.OK);
        Assert.AreEqual(0m, confirmed.GetProperty("fee").GetDecimal());

        using var scope = ProgramTest.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var payment = await db.BankPayments.Include(p => p.Transfers).SingleAsync(p => p.OrderId == confirmed.GetProperty("orderId").GetInt32());
        Assert.IsTrue(payment.Transfers.Count >= 2);
        Assert.AreEqual(total, payment.Transfers.Sum(t => t.Amount));

        var cancelled = await ReadAsync(await shopper.PostAsync($"/api/orders/{payment.OrderId}/cancel", null), HttpStatusCode.OK);
        Assert.AreEqual(total, cancelled.GetProperty("refunded").GetDecimal());
    }

    [TestMethod]
    public async Task Payment_DeclinedByBank_Answers4xx()
    {
        var shopper = ClientFor(NewShopper());
        var connectionId = await ConnectAsync(shopper, [Products.Auth], "bank"); // default sandbox checking holds $100
        var started = await ReadAsync(await shopper.PostAsJsonAsync("/api/checkout/bank-payment",
            new { connectionId, items = new[] { new { catalogItemId = 2, quantity = 100 } }, shippingCost = 0m }), HttpStatusCode.Created);
        var response = await shopper.PostAsJsonAsync($"/api/checkout/bank-payment/{started.GetProperty("checkoutId").GetGuid()}/confirm", new { speed = "standard" });
        var status = (int)response.StatusCode;
        Assert.IsTrue(status is >= 400 and < 500, await response.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------ 8 and 9

    [TestMethod]
    public async Task Support_SeesBankActivityAndTransactionsBetweenDates()
    {
        var email = NewShopper();
        var shopper = ClientFor(email);
        var admin = ClientFor("admin@microsoft.com", "Administrators");
        await ConnectAsync(shopper, [Products.Auth, Products.Transactions], "bank");
        // A bank connected without transactions (as before these look-ups existed).
        await ConnectAsync(shopper, [Products.Auth], "bank");

        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync($"/api/support/shoppers/{email}/bank-activity")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await ProgramTest.NewClient.GetAsync($"/api/support/shoppers/{email}/bank-activity")).StatusCode);

        var activity = await ReadAsync(await admin.GetAsync($"/api/support/shoppers/{email}/bank-activity"), HttpStatusCode.OK);
        var transactions = activity.GetProperty("transactions").EnumerateArray().ToList();
        Assert.IsTrue(transactions.Count > 0);
        foreach (var t in transactions)
        {
            Assert.IsFalse(string.IsNullOrEmpty(t.GetProperty("name").GetString()));
            t.GetProperty("amount").GetDecimal();
            DateOnly.Parse(t.GetProperty("date").GetString()!);
            t.GetProperty("pending").GetBoolean();
        }
        // Both banks are covered.
        Assert.AreEqual(2, transactions.Select(t => t.GetProperty("connectionId").GetInt32()).Distinct().Count(), string.Join(",", transactions.GroupBy(t => t.GetProperty("connectionId").GetInt32()).Select(g => g.Key + ":" + g.Count())));

        // Syncing again keeps the copy in step without duplicating anything.
        var again = await ReadAsync(await admin.GetAsync($"/api/support/shoppers/{email}/bank-activity"), HttpStatusCode.OK);
        Assert.IsTrue(again.GetProperty("transactions").GetArrayLength() >= transactions.Count);

        var to = DateTime.UtcNow.Date;
        var from = to.AddDays(-60);
        var lookup = await ReadAsync(await admin.GetAsync($"/api/support/shoppers/{email}/transactions?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"), HttpStatusCode.OK);
        var found = lookup.GetProperty("transactions").EnumerateArray().ToList();
        Assert.IsTrue(found.Count > 0);
        Assert.AreEqual(2, found.Select(t => t.GetProperty("connectionId").GetInt32()).Distinct().Count());
        foreach (var t in found)
        {
            var date = DateOnly.Parse(t.GetProperty("date").GetString()!);
            Assert.IsTrue(date >= DateOnly.FromDateTime(from) && date <= DateOnly.FromDateTime(to));
        }

        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/support/shoppers/{email}/transactions?from=2026-02-01&to=2026-01-01")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/support/shoppers/{email}/transactions?from=yesterday&to=today")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/support/shoppers/{NewShopper()}/bank-activity")).StatusCode);
    }

    // ------------------------------------------------------------------ 10

    [TestMethod]
    public async Task Portfolio_ListsHoldingsWithQuantityOrContracts()
    {
        var shopper = ClientFor(NewShopper());
        var empty = await ReadAsync(await shopper.GetAsync("/api/portfolio"), HttpStatusCode.OK);
        Assert.AreEqual(0, empty.GetProperty("positions").GetArrayLength());

        await ConnectAsync(shopper, [Products.Investments], "brokerage");
        var portfolio = await ReadAsync(await shopper.GetAsync("/api/portfolio"), HttpStatusCode.OK);
        var positions = portfolio.GetProperty("positions").EnumerateArray().ToList();
        Assert.IsTrue(positions.Count > 0);
        foreach (var p in positions)
        {
            p.GetProperty("value").GetDecimal();
            var hasQuantity = p.TryGetProperty("quantity", out _);
            var hasContracts = p.TryGetProperty("contracts", out _);
            Assert.IsTrue(hasQuantity ^ hasContracts, p.ToString());
        }

        // The sandbox brokerage holds 10,000 units (100 contracts) of an option: report contracts, not units.
        var option = positions.Single(p => p.GetProperty("ticker").GetString() == "NFLX180201C00355000");
        Assert.AreEqual(100m, option.GetProperty("contracts").GetDecimal());
        Assert.IsFalse(option.TryGetProperty("quantity", out _));
        Assert.AreEqual(110m, option.GetProperty("value").GetDecimal());
        var share = positions.First(p => p.TryGetProperty("quantity", out _));
        Assert.IsFalse(share.TryGetProperty("contracts", out _));
    }

    // ------------------------------------------------------------------ 3

    [TestMethod]
    public async Task Disconnect_RemovesTheItemAtPlaid()
    {
        var shopper = ClientFor(NewShopper());
        var connectionId = await ConnectAsync(shopper, [Products.Auth], "bank");

        string accessToken;
        using (var scope = ProgramTest.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
            var connection = await db.BankConnections.SingleAsync(c => c.Id == connectionId);
            accessToken = scope.ServiceProvider.GetRequiredService<Microsoft.eShopWeb.PublicApi.PayByBank.BankTokenProtector>().Unprotect(connection);
        }

        Assert.AreEqual(HttpStatusCode.NotFound, (await ClientFor(NewShopper()).DeleteAsync($"/api/bank/connections/{connectionId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, (await shopper.DeleteAsync($"/api/bank/connections/{connectionId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.DeleteAsync($"/api/bank/connections/{connectionId}")).StatusCode);

        // Plaid no longer knows the Item, so it can no longer be billed.
        await Assert.ThrowsExceptionAsync<ThePlaidApi.Core.Exceptions.ApiException<ThePlaidApi.Core.ErrorResponse.RawError>>(() =>
            SandboxClient().AccountsGet(new AccountsGetOperationRequest { Body = new AccountsGetRequest { AccessToken = accessToken } }));
    }
}
