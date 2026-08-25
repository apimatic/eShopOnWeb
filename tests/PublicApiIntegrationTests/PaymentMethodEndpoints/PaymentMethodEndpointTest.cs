using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentMethodEndpoints;

[TestClass]
public class PaymentMethodEndpointTest
{
    [TestMethod]
    public async Task ListReturnsUnauthorizedWithNoToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/payment-methods");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListReturnsOkForAuthenticatedShopper()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/payment-methods");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task DeletingUnknownPaymentMethodReturnsNotFound()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.DeleteAsync("api/payment-methods/999999");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
