using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// Checks that need no call to Stax: who may call what, and the 4xx answers for requests the app can't carry out.
/// </summary>
[TestClass]
public class PaymentEndpointsAuthTest
{
    private static HttpClient ClientFor(string? token)
    {
        var client = ProgramTest.NewClient;
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task ShopperRoutesNeedAToken()
    {
        var response = await ClientFor(null).GetAsync("api/payment-methods");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow("api/admin/orders/00000000-0000-0000-0000-000000000000/refunds")]
    [DataRow("api/admin/invoice-schedules")]
    [DataRow("api/admin/sellers")]
    public async Task StaffRoutesRefuseShoppers(string route)
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).PostAsync(route, Json("{}"));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task EmptyOrderIsRefused()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).PostAsync("api/orders", Json("{\"items\":[]}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UnknownOrderIsNotFound()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/orders/00000000-0000-0000-0000-000000000000");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task InvalidMonthlyInvoiceIsRefused()
    {
        var response = await ClientFor(ApiTokenHelper.GetAdminUserToken())
            .PostAsync("api/admin/invoice-schedules", Json("{\"email\":\"not-an-email\",\"company\":\"Acme\",\"amount\":10}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
