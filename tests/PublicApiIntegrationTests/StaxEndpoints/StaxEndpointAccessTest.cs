using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.StaxEndpoints;

/// <summary>Checks that need no call to Stax: authentication, roles and input validation.</summary>
[TestClass]
public class StaxEndpointAccessTest
{
    private static HttpClient Client(string? token)
    {
        var client = ProgramTest.NewClient;
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    [DataRow("/api/payment-methods")]
    [DataRow("/api/orders/1")]
    [DataRow("/api/admin/orders/1")]
    [DataRow("/api/admin/shoppers/a@b.com/payment-methods")]
    public async Task RequiresAToken(string url)
    {
        var response = await Client(null).GetAsync(url);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    [DataRow("/api/admin/orders/1")]
    [DataRow("/api/admin/shoppers/a@b.com/payment-methods")]
    public async Task AdminRoutesRejectShoppers(string url)
    {
        var response = await Client(ApiTokenHelper.GetNormalUserToken()).GetAsync(url);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task OrderWithoutItemsIsRejected()
    {
        var response = await Client(ApiTokenHelper.GetNormalUserToken()).PostAsJsonAsync("/api/orders", new { items = new object[0] });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UnknownOrderIsNotFound()
    {
        var response = await Client(ApiTokenHelper.GetNormalUserToken()).GetAsync("/api/orders/999999");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task InvalidInvoiceScheduleIsRejected()
    {
        var response = await Client(ApiTokenHelper.GetAdminUserToken())
            .PostAsJsonAsync("/api/admin/invoice-schedules", new { email = "not-an-email", company = "Acme", amount = 10 });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
