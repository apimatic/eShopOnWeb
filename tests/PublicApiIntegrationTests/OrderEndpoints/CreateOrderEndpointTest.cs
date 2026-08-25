using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class CreateOrderEndpointTest
{
    [TestMethod]
    public async Task ReturnsOrderIdAndAwaitingPaymentStatus()
    {
        var token = ApiTokenHelper.GetNormalUserToken();
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(new { Items = new[] { new { CatalogItemId = 1, Quantity = 1 } } }),
            Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/orders", jsonContent);
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<CreateOrderResponse>();

        Assert.IsTrue(model!.OrderId > 0);
        Assert.AreEqual("AwaitingPayment", model.Order!.Status);
        Assert.IsNull(model.Order.Payment);
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(new { Items = new[] { new { CatalogItemId = 1, Quantity = 1 } } }),
            Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/orders", jsonContent);

        Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
