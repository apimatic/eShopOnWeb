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

namespace PublicApiIntegrationTests.BankEndpoints;

/// <summary>
/// End-to-end checks against the real Plaid sandbox. The credentials come from the Plaid: configuration section
/// (user-secrets, or the PLAID_CLIENT_ID / PLAID_SECRET / PLAID_ENV environment variables, which are mapped to it).
/// </summary>
[TestClass]
public class BankPaymentSandboxTests
{
    private static ThePlaidApiClient? _plaid;

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        MapEnvironment("PLAID_CLIENT_ID", "Plaid__ClientId");
        MapEnvironment("PLAID_SECRET", "Plaid__Secret");
        MapEnvironment("PLAID_ENV", "Plaid__Environment");

        var id = Environment.GetEnvironmentVariable("Plaid__ClientId");
        var secret = Environment.GetEnvironmentVariable("Plaid__Secret");
        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(secret))
        {
            _plaid = new ThePlaidApiClient(new HttpClient(), new ThePlaidApiClientOptions
            {
                ClientId = id,
                Secret = secret,
                PlaidVersion = "2020-09-14",
                Environment = ThePlaidApi.Servers.ServerEnvironment.Environment2
            });
        }
    }

    private static void MapEnvironment(string from, string to)
    {
        var value = Environment.GetEnvironmentVariable(from);
        if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(to)))
        {
            Environment.SetEnvironmentVariable(to, value);
        }
    }

    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<string> ConnectAsync(HttpClient client, string institution, Products product, string? kind = null)
    {
        Assert.IsNotNull(_plaid, "Plaid sandbox credentials are not configured.");
        var token = await _plaid.SandboxPublicTokenCreate(new SandboxPublicTokenCreateOperationRequest
        {
            Body = new SandboxPublicTokenCreateRequest { InstitutionId = institution, InitialProducts = new[] { product } }
        });
        var response = await client.PostAsJsonAsync("api/bank/connections", new { publicToken = token.PublicToken, kind });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await Json(response)).GetProperty("connectionId").GetString()!;
    }

    [TestMethod]
    public async Task BankEndpointsNeedAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/banks");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SupportEndpointsAreForAdministrators()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/support/shoppers/demouser@microsoft.com/bank-activity");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task TransactionsBetweenDatesNeedValidDates()
    {
        var client = Client(ApiTokenHelper.GetAdminUserToken());
        var response = await client.GetAsync("api/support/shoppers/demouser@microsoft.com/transactions?from=2026-02-30&to=2026-03-01");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        response = await client.GetAsync("api/support/shoppers/demouser@microsoft.com/transactions?from=2026-03-02&to=2026-03-01");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task BankPickerSearchesByName()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/banks?query=platypus");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var banks = (await Json(response)).GetProperty("banks").EnumerateArray().ToList();
        Assert.IsTrue(banks.Count > 0);
        Assert.IsTrue(banks.All(b => b.GetProperty("name").GetString()!.Contains("Platypus", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task LinkTokenIsCreatedForTheCaller()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsJsonAsync("api/bank/link-token", new { });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        StringAssert.StartsWith((await Json(response)).GetProperty("linkToken").GetString(), "link-");
    }

    [TestMethod]
    public async Task PayConfirmAndCancelAnOrder()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var connection = await ConnectAsync(client, "ins_109508", Products.Transfer);

        var start = await client.PostAsJsonAsync("api/checkout/bank-payment", new
        {
            connectionId = connection,
            items = new[] { new { catalogItemId = 1, quantity = 2 } },
            shippingCost = 4.99m
        });
        Assert.AreEqual(HttpStatusCode.Created, start.StatusCode, await start.Content.ReadAsStringAsync());
        var started = await Json(start);
        var checkout = started.GetProperty("checkoutId").GetString()!;
        CollectionAssert.AreEqual(new[] { "standard", "express" },
            started.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToArray());

        var shipping = await client.PutAsJsonAsync($"api/checkout/bank-payment/{checkout}/shipping", new { shippingCost = 9.99m });
        Assert.AreEqual(HttpStatusCode.OK, shipping.StatusCode);
        var newTotal = (await Json(shipping)).GetProperty("total").GetDecimal();

        var confirm = await client.PostAsJsonAsync($"api/checkout/bank-payment/{checkout}/confirm", new { speed = "express" });
        Assert.AreEqual(HttpStatusCode.OK, confirm.StatusCode, await confirm.Content.ReadAsStringAsync());
        var confirmation = await Json(confirm);
        Assert.AreEqual(newTotal, confirmation.GetProperty("total").GetDecimal());
        Assert.IsTrue(confirmation.GetProperty("fee").GetDecimal() > 0m);
        var arrives = DateTime.Parse(confirmation.GetProperty("arrivesOn").GetString()!);
        Assert.IsTrue(arrives.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));
        var orderId = confirmation.GetProperty("orderId").GetInt32();

        // Confirming again must not charge again.
        var again = await client.PostAsJsonAsync($"api/checkout/bank-payment/{checkout}/confirm", new { speed = "express" });
        Assert.AreEqual(orderId, (await Json(again)).GetProperty("orderId").GetInt32());

        var cancel = await client.PostAsync($"api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode, await cancel.Content.ReadAsStringAsync());
        Assert.AreEqual(newTotal + confirmation.GetProperty("fee").GetDecimal(), (await Json(cancel)).GetProperty("refunded").GetDecimal());

        await client.DeleteAsync($"api/bank/connections/{connection}");
    }

    [TestMethod]
    public async Task ExpressIsNotOfferedAboveTheSameDayLimit()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var connection = await ConnectAsync(client, "ins_109508", Products.Transfer);
        var start = await client.PostAsJsonAsync("api/checkout/bank-payment", new
        {
            connectionId = connection,
            items = new[] { new { catalogItemId = 1, quantity = 100000 } },
            shippingCost = 0m
        });
        Assert.AreEqual(HttpStatusCode.Created, start.StatusCode);
        CollectionAssert.AreEqual(new[] { "standard" },
            (await Json(start)).GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToArray());
        await client.DeleteAsync($"api/bank/connections/{connection}");
    }

    [TestMethod]
    public async Task SupportSeesBankActivityAndTransactionsBetweenDates()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var connection = await ConnectAsync(shopper, "ins_109508", Products.Transfer);
        var support = Client(ApiTokenHelper.GetAdminUserToken());

        var activity = await support.GetAsync("api/support/shoppers/demouser@microsoft.com/bank-activity");
        Assert.AreEqual(HttpStatusCode.OK, activity.StatusCode, await activity.Content.ReadAsStringAsync());
        var list = (await Json(activity)).GetProperty("transactions").EnumerateArray().ToList();
        Assert.IsTrue(list.Count > 0);
        Assert.IsTrue(list.All(t => t.TryGetProperty("pending", out _) && t.TryGetProperty("name", out _)));

        var from = DateTime.UtcNow.AddYears(-1).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var range = await support.GetAsync($"api/support/shoppers/demouser@microsoft.com/transactions?from={from}&to={to}");
        Assert.AreEqual(HttpStatusCode.OK, range.StatusCode, await range.Content.ReadAsStringAsync());
        Assert.IsTrue((await Json(range)).GetProperty("transactions").GetArrayLength() > 0);

        await shopper.DeleteAsync($"api/bank/connections/{connection}");
    }

    [TestMethod]
    public async Task PortfolioShowsOptionsInContracts()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var connection = await ConnectAsync(client, "ins_115616", Products.Investments, "brokerage");
        var response = await client.GetAsync("api/portfolio");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var positions = (await Json(response)).GetProperty("positions").EnumerateArray().ToList();
        var option = positions.Single(p => p.TryGetProperty("contracts", out _));
        Assert.AreEqual(100m, option.GetProperty("contracts").GetDecimal());
        Assert.IsFalse(option.TryGetProperty("quantity", out _));
        Assert.IsTrue(positions.Where(p => !p.TryGetProperty("contracts", out _)).All(p => p.TryGetProperty("quantity", out _)));
        await client.DeleteAsync($"api/bank/connections/{connection}");
    }
}
