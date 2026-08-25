using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.PaymentMethodEndpoints;

[TestClass]
public class PaymentMethodEndpointsTest
{
    [TestMethod]
    public async Task ListReturnsUnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync("api/payment-methods");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListIsEmptyForNewShopper()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetUserToken("no-cards-yet@microsoft.com"));

        var response = await client.GetAsync("api/payment-methods");

        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadAsStringAsync()).FromJson<ListPaymentMethodsResponse>();
        Assert.AreEqual(0, body!.PaymentMethods.Count);
    }

    [TestMethod]
    public async Task DeleteReturnsNotFoundForUnknownCard()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.DeleteAsync("api/payment-methods/999999");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
