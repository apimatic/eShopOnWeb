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
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;
using ThePlaidApi.Servers;

namespace PublicApiIntegrationTests.BankEndpoints;

/// <summary>
/// End-to-end tests of the pay-by-bank endpoints. They call the real Plaid sandbox (credentials come from the
/// PLAID_CLIENT_ID / PLAID_SECRET environment variables, or the Plaid:* user-secrets of the PublicApi project).
/// </summary>
[TestClass]
public class PayByBankTests
{
    private static readonly string[] Admin = { "Administrators" };

    private static string Token(string email, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, email) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY)),
                SecurityAlgorithms.HmacSha256Signature)
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private static HttpClient ClientFor(string email, params string[] roles)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(email, roles));
        return client;
    }

    private static string NewShopper() => $"shopper-{Guid.NewGuid():N}@example.com";

    private static ThePlaidApiClient Plaid()
    {
        var clientId = Environment.GetEnvironmentVariable("PLAID_CLIENT_ID");
        var secret = Environment.GetEnvironmentVariable("PLAID_SECRET");
        Assert.IsFalse(string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(secret), "PLAID_CLIENT_ID / PLAID_SECRET are not set.");
        return new ThePlaidApiClient(new HttpClient(), new ThePlaidApiClientOptions
        {
            Environment = ServerEnvironment.Environment2,
            ClientId = clientId,
            Secret = secret,
            PlaidVersion = "2020-09-14",
        });
    }

    /// <summary>What Plaid Link hands the browser, made in the sandbox.</summary>
    private static async Task<string> PublicTokenAsync(Products product)
    {
        var result = await Plaid().SandboxPublicTokenCreate(new SandboxPublicTokenCreateOperationRequest
        {
            Body = new SandboxPublicTokenCreateRequest { InstitutionId = "ins_109508", InitialProducts = [product] }
        });
        return result.PublicToken;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(text.Length > 0, $"empty body ({(int)response.StatusCode})");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<int> ConnectAsync(HttpClient client, Products product, string? type = null)
    {
        var response = await client.PostAsJsonAsync("api/bank/connections", new { publicToken = await PublicTokenAsync(product), type });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await Json(response)).GetProperty("connectionId").GetInt32();
    }

    private static object[] Items(int quantity = 2) => new object[] { new { catalogItemId = 1, quantity } };

    [TestMethod]
    public async Task BankPickerListsAndSearchesBanks()
    {
        var client = ClientFor(NewShopper());

        var allResponse = await client.GetAsync("api/banks");
        Assert.AreEqual(HttpStatusCode.OK, allResponse.StatusCode, await allResponse.Content.ReadAsStringAsync());
        var all = await Json(allResponse);
        Assert.IsTrue(all.GetProperty("banks").GetArrayLength() > 0);
        Assert.IsTrue(all.GetProperty("total").GetInt32() > 1000);

        var found = await Json(await client.GetAsync("api/banks?query=chase"));
        var names = found.GetProperty("banks").EnumerateArray().Select(b => b.GetProperty("name").GetString()).ToList();
        Assert.IsTrue(names.Any(n => n!.Contains("Chase", StringComparison.OrdinalIgnoreCase)), string.Join(",", names));
    }

    [TestMethod]
    public async Task LinkTokenIsCreatedForBankAndBrokerage()
    {
        var client = ClientFor(NewShopper());
        foreach (var type in new[] { "bank", "brokerage" })
        {
            var response = await client.PostAsJsonAsync("api/bank/link-token", new { type });
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            Assert.IsTrue((await Json(response)).GetProperty("linkToken").GetString()!.StartsWith("link-"));
        }
        var bare = await client.PostAsync("api/bank/link-token", null);
        Assert.AreEqual(HttpStatusCode.OK, bare.StatusCode);
    }

    [TestMethod]
    public async Task PayExpressCancelAndRefund()
    {
        var shopper = NewShopper();
        var client = ClientFor(shopper);
        var connectionId = await ConnectAsync(client, Products.Transactions);

        var start = await client.PostAsJsonAsync("api/checkout/bank-payment", new { connectionId, items = Items(), shippingCost = 5.00m });
        Assert.AreEqual(HttpStatusCode.Created, start.StatusCode, await start.Content.ReadAsStringAsync());
        var quote = await Json(start);
        var checkoutId = quote.GetProperty("checkoutId").GetString();
        var total = quote.GetProperty("total").GetDecimal();
        var options = quote.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToList();
        CollectionAssert.AreEquivalent(new[] { "standard", "express" }, options);

        // Shipping changes after the start: confirming charges the new total.
        var changed = await client.PutAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/shipping", new { shippingCost = 7.50m });
        Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode, await changed.Content.ReadAsStringAsync());
        Assert.AreEqual(total + 2.50m, (await Json(changed)).GetProperty("total").GetDecimal());

        var confirm = await client.PostAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" });
        Assert.AreEqual(HttpStatusCode.OK, confirm.StatusCode, await confirm.Content.ReadAsStringAsync());
        var order = await Json(confirm);
        var orderId = order.GetProperty("orderId").GetInt32();
        Assert.IsTrue(order.GetProperty("fee").GetDecimal() > 0);
        Assert.AreEqual(total + 2.50m + order.GetProperty("fee").GetDecimal(), order.GetProperty("charged").GetDecimal());
        Assert.IsTrue(DateOnly.TryParse(order.GetProperty("arrivesOn").GetString(), out _));

        // Confirming twice never charges twice.
        var again = await client.PostAsJsonAsync($"api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" });
        Assert.AreEqual(orderId, (await Json(again)).GetProperty("orderId").GetInt32());

        // Someone else cannot cancel it ...
        var stranger = await ClientFor(NewShopper()).PostAsync($"api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.NotFound, stranger.StatusCode);

        // ... the shopper can, and gets everything back, fee included.
        var cancel = await client.PostAsync($"api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode, await cancel.Content.ReadAsStringAsync());
        Assert.AreEqual(order.GetProperty("charged").GetDecimal(), (await Json(cancel)).GetProperty("refunded").GetDecimal());

        var cancelAgain = await client.PostAsync($"api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.Conflict, cancelAgain.StatusCode);
    }

    [TestMethod]
    public async Task WholesaleOrderCanOnlyGoStandard()
    {
        var client = ClientFor(NewShopper());
        var connectionId = await ConnectAsync(client, Products.Transactions);

        var start = await client.PostAsJsonAsync("api/checkout/bank-payment",
            new { connectionId, items = Items(quantity: 5_000_000), shippingCost = 0 });
        Assert.AreEqual(HttpStatusCode.Created, start.StatusCode, await start.Content.ReadAsStringAsync());
        var quote = await Json(start);
        CollectionAssert.AreEqual(new[] { "standard" }, quote.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToArray());

        var express = await client.PostAsJsonAsync($"api/checkout/bank-payment/{quote.GetProperty("checkoutId").GetString()}/confirm", new { speed = "express" });
        Assert.AreEqual(HttpStatusCode.BadRequest, express.StatusCode);
    }

    [TestMethod]
    public async Task CheckoutRejectsOtherShoppersConnectionAndBadInput()
    {
        var owner = ClientFor(NewShopper());
        var connectionId = await ConnectAsync(owner, Products.Transactions);

        var other = ClientFor(NewShopper());
        var stolen = await other.PostAsJsonAsync("api/checkout/bank-payment", new { connectionId, items = Items(), shippingCost = 1 });
        Assert.AreEqual(HttpStatusCode.NotFound, stolen.StatusCode);

        var noItems = await owner.PostAsJsonAsync("api/checkout/bank-payment", new { connectionId, items = Array.Empty<object>(), shippingCost = 1 });
        Assert.AreEqual(HttpStatusCode.BadRequest, noItems.StatusCode);

        var negative = await owner.PostAsJsonAsync("api/checkout/bank-payment", new { connectionId, items = Items(), shippingCost = -1 });
        Assert.AreEqual(HttpStatusCode.BadRequest, negative.StatusCode);

        var unknownItem = await owner.PostAsJsonAsync("api/checkout/bank-payment",
            new { connectionId, items = new[] { new { catalogItemId = 987654, quantity = 1 } }, shippingCost = 1 });
        Assert.AreEqual(HttpStatusCode.NotFound, unknownItem.StatusCode);
    }

    [TestMethod]
    public async Task SupportSeesBankActivityAndTransactionsBetweenDates()
    {
        var shopper = NewShopper();
        var client = ClientFor(shopper);
        await ConnectAsync(client, Products.Transactions);
        await ConnectAsync(client, Products.Auth); // a second bank, linked without the transactions product

        var support = ClientFor("admin@microsoft.com", Admin);

        var activity = await support.GetAsync($"api/support/shoppers/{shopper}/bank-activity");
        Assert.AreEqual(HttpStatusCode.OK, activity.StatusCode, await activity.Content.ReadAsStringAsync());
        var transactions = (await Json(activity)).GetProperty("transactions");
        Assert.IsTrue(transactions.GetArrayLength() > 0);
        var first = transactions[0];
        Assert.IsTrue(first.TryGetProperty("name", out _) && first.TryGetProperty("amount", out _)
                      && first.TryGetProperty("date", out _) && first.TryGetProperty("pending", out _));
        var connections = transactions.EnumerateArray().Select(t => t.GetProperty("connectionId").GetInt32()).Distinct().Count();
        Assert.AreEqual(2, connections, "every connected bank is covered");

        // Syncing again from the stored cursor must not duplicate anything.
        var again = await Json(await support.GetAsync($"api/support/shoppers/{shopper}/bank-activity"));
        Assert.AreEqual(transactions.GetArrayLength(), again.GetProperty("transactions").GetArrayLength());

        var from = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var between = await support.GetAsync($"api/support/shoppers/{shopper}/transactions?from={from}&to={to}");
        Assert.AreEqual(HttpStatusCode.OK, between.StatusCode, await between.Content.ReadAsStringAsync());
        var list = (await Json(between)).GetProperty("transactions");
        Assert.IsTrue(list.GetArrayLength() > 0);
        foreach (var t in list.EnumerateArray())
        {
            var date = DateOnly.Parse(t.GetProperty("date").GetString()!);
            Assert.IsTrue(date >= DateOnly.Parse(from) && date <= DateOnly.Parse(to), date.ToString());
        }

        var backwards = await support.GetAsync($"api/support/shoppers/{shopper}/transactions?from={to}&to={from}");
        Assert.AreEqual(HttpStatusCode.BadRequest, backwards.StatusCode);
        var badDate = await support.GetAsync($"api/support/shoppers/{shopper}/transactions?from=yesterday&to={to}");
        Assert.AreEqual(HttpStatusCode.BadRequest, badDate.StatusCode);

        // Only the administrator role may look.
        var notStaff = await client.GetAsync($"api/support/shoppers/{shopper}/bank-activity");
        Assert.AreEqual(HttpStatusCode.Forbidden, notStaff.StatusCode);
        var unknown = await support.GetAsync($"api/support/shoppers/{NewShopper()}/bank-activity");
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [TestMethod]
    public async Task PortfolioListsHoldingsOfTheBrokerageAccount()
    {
        var client = ClientFor(NewShopper());

        var none = await client.GetAsync("api/portfolio");
        Assert.AreEqual(HttpStatusCode.NotFound, none.StatusCode);

        await ConnectAsync(client, Products.Investments);
        var response = await client.GetAsync("api/portfolio");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var positions = (await Json(response)).GetProperty("positions").EnumerateArray().ToList();
        Assert.IsTrue(positions.Count > 0);
        foreach (var p in positions)
        {
            Assert.IsTrue(p.TryGetProperty("value", out _));
            Assert.IsTrue(p.TryGetProperty("ticker", out _) && p.TryGetProperty("name", out _));
            Assert.AreNotEqual(p.TryGetProperty("quantity", out _), p.TryGetProperty("contracts", out _),
                "a position has either a quantity or contracts");
        }
        Assert.IsTrue(positions.Any(p => p.TryGetProperty("contracts", out _)), "the sandbox portfolio holds options");
    }

    [TestMethod]
    public async Task DisconnectedBankCannotPayAnyMore()
    {
        var client = ClientFor(NewShopper());
        var connectionId = await ConnectAsync(client, Products.Transactions);

        var stranger = await ClientFor(NewShopper()).DeleteAsync($"api/bank/connections/{connectionId}");
        Assert.AreEqual(HttpStatusCode.NotFound, stranger.StatusCode);

        var delete = await client.DeleteAsync($"api/bank/connections/{connectionId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode, await delete.Content.ReadAsStringAsync());

        var start = await client.PostAsJsonAsync("api/checkout/bank-payment", new { connectionId, items = Items(), shippingCost = 1 });
        Assert.AreEqual(HttpStatusCode.NotFound, start.StatusCode);
        var deleteAgain = await client.DeleteAsync($"api/bank/connections/{connectionId}");
        Assert.AreEqual(HttpStatusCode.NotFound, deleteAgain.StatusCode);
    }

    [TestMethod]
    public async Task EndpointsRequireAToken()
    {
        var client = ProgramTest.NewClient;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/banks")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/portfolio")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsync("api/orders/1/cancel", null)).StatusCode);
    }
}
