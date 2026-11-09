using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>Checks that need no call to Stax: authentication, roles and request validation.</summary>
[TestClass]
public class PaymentEndpointsTest
{
    private static HttpClient ClientFor(string? token)
    {
        var client = ProgramTest.NewClient;
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    [TestMethod]
    [DataRow("GET", "api/payment-methods")]
    [DataRow("POST", "api/orders")]
    [DataRow("GET", "api/orders/1")]
    [DataRow("POST", "api/account/close")]
    [DataRow("GET", "api/admin/orders/1")]
    [DataRow("POST", "api/admin/orders/1/refunds")]
    [DataRow("POST", "api/admin/invoice-schedules")]
    [DataRow("POST", "api/admin/sellers")]
    public async Task RequiresAToken(string method, string url)
    {
        var response = await ClientFor(null).SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    [DataRow("GET", "api/admin/orders/1")]
    [DataRow("GET", "api/admin/shoppers/demouser@microsoft.com/payment-methods")]
    [DataRow("POST", "api/admin/orders/1/refunds")]
    [DataRow("POST", "api/admin/invoice-schedules")]
    [DataRow("POST", "api/admin/invoice-schedules/abc/pause")]
    [DataRow("POST", "api/admin/invoice-schedules/abc/resume")]
    [DataRow("POST", "api/admin/sellers")]
    public async Task StaffRoutesRejectShoppers(string method, string url)
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task OrderWithoutItemsIsRejected()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).PostAsync("api/orders", Json("{\"items\":[]}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task OrderForUnknownCatalogItemIsNotFound()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken())
            .PostAsync("api/orders", Json("{\"items\":[{\"catalogItemId\":987654,\"units\":1}]}"));
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task OrderOfAnotherShopperIsNotFound()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/orders/123456");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task PaymentMethodWithBadCardNumberIsRejected()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).PostAsync("api/payment-methods",
            Json("{\"method\":\"card\",\"personName\":\"A B\",\"cardNumber\":\"12\",\"cardExp\":\"1230\"}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task InvoiceScheduleInThePastIsRejected()
    {
        var response = await ClientFor(ApiTokenHelper.GetAdminUserToken()).PostAsync("api/admin/invoice-schedules",
            Json("{\"email\":\"a@b.example\",\"company\":\"Acme\",\"amount\":10,\"firstInvoiceDate\":\"2001-01-01\"}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task RefundOfUnknownOrderIsNotFound()
    {
        var response = await ClientFor(ApiTokenHelper.GetAdminUserToken()).PostAsync("api/admin/orders/987654/refunds", null);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
