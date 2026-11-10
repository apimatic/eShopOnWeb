using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThePlaidApi;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Servers;

namespace PublicApiIntegrationTests.BankEndpoints;

/// <summary>
/// Pay-by-bank tests. They talk to the real Plaid sandbox: the API under test reads its credentials from the
/// <c>Plaid:</c> configuration (user-secrets) and the tests create sandbox items with the same credentials taken
/// from the PLAID_CLIENT_ID / PLAID_SECRET / PLAID_ENV environment variables.
/// </summary>
[TestClass]
public class PayByBankTests
{
    private const string FirstPlatypusBank = "ins_109508";

    private static ThePlaidApiClient SandboxClient()
    {
        var clientId = Environment.GetEnvironmentVariable("PLAID_CLIENT_ID");
        var secret = Environment.GetEnvironmentVariable("PLAID_SECRET");
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(secret))
            Assert.Inconclusive("PLAID_CLIENT_ID and PLAID_SECRET are not set.");

        return new ThePlaidApiClient(new HttpClient(), new ThePlaidApiClientOptions
        {
            Environment = ServerEnvironment.Environment2,
            ClientId = clientId,
            Secret = secret,
            PlaidVersion = "2020-09-14"
        });
    }

    private static async Task<string> SandboxPublicToken(string institution, params Products[] products)
    {
        var response = await SandboxClient().SandboxPublicTokenCreate(new()
        {
            Body = new() { InstitutionId = institution, InitialProducts = products }
        });
        return response.PublicToken;
    }

    private static string Token(string userName, params string[] roles)
    {
        var claims = new[] { new Claim(ClaimTypes.Name, userName) }
            .Concat(roles.Select(r => new Claim(ClaimTypes.Role, r))).ToArray();
        var key = Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY);
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        }));
    }

    private static HttpClient ClientFor(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string NewShopper() => $"shopper-{Guid.NewGuid():N}@example.com";

    private static async Task<JsonNode> Json(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, response.StatusCode, body);
        return JsonNode.Parse(body)!;
    }

    private static async Task<int> Connect(HttpClient shopper, string institution, string kind, params Products[] products)
    {
        var publicToken = await SandboxPublicToken(institution, products);
        var saved = await Json(await shopper.PostAsJsonAsync("/api/bank/connections", new { publicToken, kind }), HttpStatusCode.OK);
        return saved["connectionId"]!.GetValue<int>();
    }

    private static async Task<(Guid checkoutId, JsonNode quote)> StartPayment(HttpClient shopper, int connectionId, int quantity, decimal shipping)
    {
        var quote = await Json(await shopper.PostAsJsonAsync("/api/checkout/bank-payment", new
        {
            connectionId,
            items = new[] { new { catalogItemId = 1, quantity } },
            shippingCost = shipping
        }), HttpStatusCode.OK);
        return (Guid.Parse(quote["checkoutId"]!.GetValue<string>()), quote);
    }

    [TestMethod]
    public async Task BankPickerListsAndSearchesBanks()
    {
        var shopper = ClientFor(Token(NewShopper()));

        var all = await Json(await shopper.GetAsync("/api/banks"), HttpStatusCode.OK);
        Assert.IsTrue(all["banks"]!.AsArray().Count > 0);

        var found = await Json(await shopper.GetAsync("/api/banks?query=platypus"), HttpStatusCode.OK);
        Assert.IsTrue(found["banks"]!.AsArray().Any(b => b!["name"]!.GetValue<string>().Contains("Platypus")));

        var anonymous = await ProgramTest.NewClient.GetAsync("/api/banks");
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [TestMethod]
    public async Task LinkTokenIsIssuedForBankAndBrokerage()
    {
        var shopper = ClientFor(Token(NewShopper()));
        foreach (var kind in new[] { "bank", "brokerage" })
        {
            var link = await Json(await shopper.PostAsJsonAsync("/api/bank/link-token", new { kind }), HttpStatusCode.OK);
            Assert.IsTrue(link["linkToken"]!.GetValue<string>().StartsWith("link-"));
        }
        var bad = await shopper.PostAsJsonAsync("/api/bank/link-token", new { kind = "piggy" });
        Assert.AreEqual(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [TestMethod]
    public async Task PayExpressChangeShippingAndCancel()
    {
        var shopper = ClientFor(Token(NewShopper()));
        var connectionId = await Connect(shopper, FirstPlatypusBank, "bank", Products.Auth, Products.Transactions);

        var (checkoutId, quote) = await StartPayment(shopper, connectionId, 1, 2.00m);
        var options = quote["options"]!.AsArray().Select(o => o!.GetValue<string>()).ToList();
        CollectionAssert.AreEquivalent(new[] { "standard", "express" }, options);
        var firstTotal = quote["total"]!.GetValue<decimal>();

        // the shipping is recalculated: the new total is what gets charged
        var changed = await Json(await shopper.PutAsJsonAsync($"/api/checkout/bank-payment/{checkoutId}/shipping", new { shippingCost = 1.00m }),
            HttpStatusCode.OK);
        Assert.AreEqual(firstTotal - 1.00m, changed["total"]!.GetValue<decimal>());

        // the bank in the sandbox only has a small balance: an unsupported speed is refused before anything is charged
        var badSpeed = await shopper.PostAsJsonAsync($"/api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "teleport" });
        Assert.AreEqual(HttpStatusCode.BadRequest, badSpeed.StatusCode);

        var confirmation = await Json(await shopper.PostAsJsonAsync($"/api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" }),
            HttpStatusCode.OK);
        Assert.AreEqual(5.00m, confirmation["fee"]!.GetValue<decimal>());
        Assert.IsTrue(DateOnly.TryParse(confirmation["arrivesOn"]!.GetValue<string>(), out _));
        var orderId = confirmation["orderId"]!.GetValue<int>();

        // confirming again does not charge again
        var again = await Json(await shopper.PostAsJsonAsync($"/api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" }),
            HttpStatusCode.OK);
        Assert.AreEqual(orderId, again["orderId"]!.GetValue<int>());

        // somebody else cannot cancel it
        var stranger = ClientFor(Token(NewShopper()));
        Assert.AreEqual(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);

        var cancelled = await Json(await shopper.PostAsync($"/api/orders/{orderId}/cancel", null), HttpStatusCode.OK);
        Assert.AreEqual("cancelled", cancelled["status"]!.GetValue<string>());
        Assert.AreEqual(confirmation["totalCharged"]!.GetValue<decimal>(), cancelled["refunded"]!.GetValue<decimal>());
    }

    [TestMethod]
    public async Task HugeOrdersOnlyOfferStandardAndAreDeclinedWithAReason()
    {
        var shopper = ClientFor(Token(NewShopper()));
        var connectionId = await Connect(shopper, FirstPlatypusBank, "bank", Products.Auth, Products.Transactions);

        // more than one same-day transfer can carry, and more than one standard transfer can carry
        var (checkoutId, quote) = await StartPayment(shopper, connectionId, 2_000_000, 0m);
        CollectionAssert.AreEqual(new[] { "standard" }, quote["options"]!.AsArray().Select(o => o!.GetValue<string>()).ToArray());

        var express = await shopper.PostAsJsonAsync($"/api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "express" });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, express.StatusCode);

        // the sandbox account cannot cover it: Plaid declines, and the shopper hears why
        var standard = await shopper.PostAsJsonAsync($"/api/checkout/bank-payment/{checkoutId}/confirm", new { speed = "standard" });
        Assert.IsTrue((int)standard.StatusCode is >= 400 and < 500, await standard.Content.ReadAsStringAsync());
        StringAssert.Contains(await standard.Content.ReadAsStringAsync(), "message", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task SupportSeesBankActivityAndTransactionsBetweenDates()
    {
        var email = NewShopper();
        var shopper = ClientFor(Token(email));
        await Connect(shopper, FirstPlatypusBank, "bank", Products.Auth, Products.Transactions);
        var support = ClientFor(Token("support@example.com", "Administrators"));

        var activity = await Json(await support.GetAsync($"/api/support/shoppers/{Uri.EscapeDataString(email)}/bank-activity"), HttpStatusCode.OK);
        var transactions = activity["transactions"]!.AsArray();
        Assert.IsTrue(transactions.Count > 0, activity.ToJsonString());
        Assert.IsNotNull(transactions[0]!["name"]);
        Assert.IsNotNull(transactions[0]!["pending"]);

        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var from = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd");
        var ranged = await Json(await support.GetAsync($"/api/support/shoppers/{Uri.EscapeDataString(email)}/transactions?from={from}&to={to}"),
            HttpStatusCode.OK);
        Assert.IsTrue(ranged["transactions"]!.AsArray().Count > 0);

        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await support.GetAsync($"/api/support/shoppers/{Uri.EscapeDataString(email)}/transactions?from=yesterday&to={to}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync($"/api/support/shoppers/{Uri.EscapeDataString(email)}/bank-activity")).StatusCode);
    }

    [TestMethod]
    public async Task TransactionLookupWorksForBanksConnectedWithoutTransactions()
    {
        var email = NewShopper();
        var shopper = ClientFor(Token(email));
        await Connect(shopper, FirstPlatypusBank, "bank", Products.Auth);
        var support = ClientFor(Token("support@example.com", "Administrators"));

        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var from = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd");
        var ranged = await Json(await support.GetAsync($"/api/support/shoppers/{Uri.EscapeDataString(email)}/transactions?from={from}&to={to}"),
            HttpStatusCode.OK);
        Assert.IsTrue(ranged["transactions"]!.AsArray().Count > 0);
    }

    [TestMethod]
    public async Task PortfolioShowsContractsForOptionsAndQuantityForTheRest()
    {
        var shopper = ClientFor(Token(NewShopper()));
        await Connect(shopper, FirstPlatypusBank, "brokerage", Products.Investments);

        var portfolio = await Json(await shopper.GetAsync("/api/portfolio"), HttpStatusCode.OK);
        var positions = portfolio["positions"]!.AsArray();
        Assert.IsTrue(positions.Count > 0);

        var option = positions.Single(p => p!["ticker"]?.GetValue<string>() == "NFLX180201C00355000")!;
        Assert.AreEqual(100m, option["contracts"]!.GetValue<decimal>()); // Plaid reports 10000 = 100 contracts x 100
        Assert.IsNull(option["quantity"]);

        var stock = positions.First(p => p!["ticker"]?.GetValue<string>() == "ACHN")!;
        Assert.AreEqual(1m, stock["quantity"]!.GetValue<decimal>());
        Assert.IsNull(stock["contracts"]);
    }

    [TestMethod]
    public async Task DisconnectRemovesTheConnection()
    {
        var shopper = ClientFor(Token(NewShopper()));
        var connectionId = await Connect(shopper, FirstPlatypusBank, "bank", Products.Auth, Products.Transactions);

        var stranger = ClientFor(Token(NewShopper()));
        Assert.AreEqual(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/bank/connections/{connectionId}")).StatusCode);

        Assert.AreEqual(HttpStatusCode.NoContent, (await shopper.DeleteAsync($"/api/bank/connections/{connectionId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.DeleteAsync($"/api/bank/connections/{connectionId}")).StatusCode);

        var startAfter = await shopper.PostAsJsonAsync("/api/checkout/bank-payment", new
        {
            connectionId,
            items = new[] { new { catalogItemId = 1, quantity = 1 } },
            shippingCost = 0
        });
        Assert.AreEqual(HttpStatusCode.NotFound, startAfter.StatusCode);
    }
}
