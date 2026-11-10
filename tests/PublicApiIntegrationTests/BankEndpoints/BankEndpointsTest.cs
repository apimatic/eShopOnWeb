using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BankEndpoints;

[TestClass]
public class BankEndpointsTest
{
    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task EndpointsRequireAToken()
    {
        var client = ProgramTest.NewClient;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/banks")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/portfolio")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsync("api/orders/1/cancel", null)).StatusCode);
    }

    [TestMethod]
    public async Task SupportEndpointsAreForAdministrators()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var email = "demouser@microsoft.com";
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync($"api/support/shoppers/{email}/bank-activity")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync($"api/support/shoppers/{email}/transactions?from=2026-01-01&to=2026-02-01")).StatusCode);
    }

    [TestMethod]
    public async Task TransactionsRejectBadDates()
    {
        var client = Client(ApiTokenHelper.GetAdminUserToken());
        var email = "demouser@microsoft.com";
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.GetAsync($"api/support/shoppers/{email}/transactions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.GetAsync($"api/support/shoppers/{email}/transactions?from=2026-02-01&to=2026-01-01")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.GetAsync($"api/support/shoppers/{email}/transactions?from=yesterday&to=today")).StatusCode);
    }

    [TestMethod]
    public async Task UnknownShopperIsNotFound()
    {
        var client = Client(ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync("api/support/shoppers/nobody@example.com/bank-activity")).StatusCode);
    }

    [TestMethod]
    public async Task StartingAPaymentOnAnUnknownConnectionIsNotFound()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsJsonAsync("api/checkout/bank-payment", new
        {
            connectionId = "nope",
            items = new[] { new { catalogItemId = 1, quantity = 1 } },
            shippingCost = 1m
        });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task StartingAPaymentValidatesTheRequest()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var noItems = await client.PostAsJsonAsync("api/checkout/bank-payment", new { connectionId = "x", items = new object[0], shippingCost = 1m });
        Assert.AreEqual(HttpStatusCode.BadRequest, noItems.StatusCode);
        var negative = await client.PostAsJsonAsync("api/checkout/bank-payment", new { connectionId = "x", items = new[] { new { catalogItemId = 1, quantity = 1 } }, shippingCost = -1m });
        Assert.AreEqual(HttpStatusCode.BadRequest, negative.StatusCode);
    }

    [TestMethod]
    public async Task AnotherShoppersOrderCannotBeCancelled()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.PostAsync("api/orders/999/cancel", null)).StatusCode);
    }

    [TestMethod]
    public async Task BanksAnswerServiceUnavailableWhenPlaidIsNotConfigured()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("Plaid:ClientId", ""),
                new System.Collections.Generic.KeyValuePair<string, string?>("Plaid:Secret", "")
            })));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/banks?query=bank");
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    /// <summary>Runs against the real Plaid sandbox when PLAID_CLIENT_ID / PLAID_SECRET / PLAID_ENV are set.</summary>
    [TestMethod]
    public async Task SandboxBankSearchFindsPlatypusBanks()
    {
        var id = Environment.GetEnvironmentVariable("PLAID_CLIENT_ID");
        var secret = Environment.GetEnvironmentVariable("PLAID_SECRET");
        var env = Environment.GetEnvironmentVariable("PLAID_ENV");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(env))
            Assert.Inconclusive("Plaid sandbox credentials are not in the environment.");

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("Plaid:ClientId", id),
                new System.Collections.Generic.KeyValuePair<string, string?>("Plaid:Secret", secret),
                new System.Collections.Generic.KeyValuePair<string, string?>("Plaid:Environment", env)
            })));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/banks?query=platypus");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.GetProperty("banks").GetArrayLength() > 0);
    }
}
