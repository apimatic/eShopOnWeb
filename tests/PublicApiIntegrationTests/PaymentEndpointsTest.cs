using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests;

[TestClass]
public class PaymentEndpointsTest
{
    private static StringContent Json(string s) => new(s, Encoding.UTF8, "application/json");

    [DataTestMethod]
    [DataRow("GET", "api/payment-methods")]
    [DataRow("GET", "api/orders/1")]
    [DataRow("POST", "api/account/close")]
    [DataRow("GET", "api/admin/orders/1")]
    [DataRow("POST", "api/admin/sellers")]
    public async Task RequiresAToken(string method, string url)
    {
        var client = ProgramTest.NewClient;
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = Json("{}") });
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow("GET", "api/admin/orders/1")]
    [DataRow("POST", "api/admin/orders/1/refunds")]
    [DataRow("GET", "api/admin/shoppers/a@b.com/payment-methods")]
    [DataRow("POST", "api/admin/invoice-schedules")]
    [DataRow("POST", "api/admin/sellers")]
    public async Task StaffEndpointsRejectShoppers(string method, string url)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = Json("{}") });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task OrderWithoutItemsIsRejected()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders", Json("{\"items\":[]}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
