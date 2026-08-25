using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.PaymentMethodEndpoints;

[TestClass]
public class ListPaymentMethodsEndpointTest
{
    [TestMethod]
    public async Task ReturnsEmptyListForBuyerWithNoSavedCards()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetUserToken("no-cards-yet@example.com"));

        var response = await client.GetAsync("api/payment-methods");
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListPaymentMethodsResponse>();

        Assert.IsNotNull(model!.PaymentMethods);
        Assert.AreEqual(0, model.PaymentMethods.Count);
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync("api/payment-methods");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
