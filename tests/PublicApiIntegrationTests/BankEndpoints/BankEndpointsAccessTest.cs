using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.BankEndpoints;

/// <summary>Access rules and request validation; none of these reach Plaid.</summary>
[TestClass]
public class BankEndpointsAccessTest
{
    private static HttpClient ClientWith(string? token)
    {
        var client = ProgramTest.NewClient;
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task ShopperEndpointsRequireAToken()
    {
        var client = ClientWith(null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("api/bank/link-token", new { })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/portfolio")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.DeleteAsync("api/bank/connections/1")).StatusCode);
    }

    [TestMethod]
    public async Task SupportEndpointsAreForAdministratorsOnly()
    {
        var shopper = ClientWith(ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync("api/support/shoppers/a@b.c/bank-activity")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.GetAsync("api/support/shoppers/a@b.c/transactions?from=2024-01-01&to=2024-01-31")).StatusCode);
    }

    [TestMethod]
    public async Task TransactionsLookupRejectsBadDates()
    {
        var admin = ClientWith(ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.GetAsync("api/support/shoppers/a@b.c/transactions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await admin.GetAsync("api/support/shoppers/a@b.c/transactions?from=2024-02-01&to=2024-01-01")).StatusCode);
    }

    [TestMethod]
    public async Task ConnectionsAndCheckoutsBelongToTheirShopper()
    {
        var shopper = ClientWith(ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.DeleteAsync("api/bank/connections/999")).StatusCode);
        var start = await shopper.PostAsJsonAsync("api/checkout/bank-payment",
            new { connectionId = 999, items = new[] { new { catalogItemId = 1, quantity = 1 } }, shippingCost = 1 });
        Assert.AreEqual(HttpStatusCode.NotFound, start.StatusCode);
        var confirm = await shopper.PostAsJsonAsync($"api/checkout/bank-payment/{System.Guid.NewGuid()}/confirm", new { speed = "standard" });
        Assert.AreEqual(HttpStatusCode.NotFound, confirm.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.PostAsync("api/orders/12345/cancel", null)).StatusCode);
    }
}
