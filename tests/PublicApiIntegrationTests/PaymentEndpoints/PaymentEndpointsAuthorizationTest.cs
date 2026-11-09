using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentEndpointsAuthorizationTest
{
    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task ShopperEndpointsRequireAToken()
    {
        var client = ProgramTest.NewClient;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/payment-methods")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsync("api/orders", Json("{}"))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsync("api/account/close", null)).StatusCode);
    }

    [TestMethod]
    [DataRow("POST", "api/admin/orders/1/refunds")]
    [DataRow("GET", "api/admin/orders/1")]
    [DataRow("GET", "api/admin/shoppers/a@b.com/payment-methods")]
    [DataRow("POST", "api/admin/invoice-schedules")]
    [DataRow("POST", "api/admin/sellers")]
    public async Task StaffEndpointsAreForbiddenForShoppers(string method, string url)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = method == "POST" ? Json("{}") : null });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task PlacingAnOrderNeedsItems()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders", Json("{\"items\":[]}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
