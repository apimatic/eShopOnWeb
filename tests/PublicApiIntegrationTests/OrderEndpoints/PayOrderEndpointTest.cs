using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class PayOrderEndpointTest
{
    [TestMethod]
    public async Task ReturnsNotFoundWhenPayingAnotherBuyersOrder()
    {
        var ownerClient = ProgramTest.NewClient;
        ownerClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetUserToken("order-owner@example.com"));

        var createResponse = await ownerClient.PostAsync("api/orders", new StringContent(
            JsonSerializer.Serialize(new { Items = new[] { new { CatalogItemId = 1, Quantity = 1 } } }),
            Encoding.UTF8, "application/json"));
        createResponse.EnsureSuccessStatusCode();
        var order = (await createResponse.Content.ReadAsStringAsync()).FromJson<CreateOrderResponse>();

        var otherClient = ProgramTest.NewClient;
        otherClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetUserToken("someone-else@example.com"));

        var payResponse = await otherClient.PostAsync($"api/orders/{order!.OrderId}/pay", new StringContent(
            JsonSerializer.Serialize(new { SavedPaymentMethodId = 1 }), Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.NotFound, payResponse.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsNotFoundForNonexistentOrder()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/orders/999999/pay", new StringContent(
            JsonSerializer.Serialize(new { SavedPaymentMethodId = 1 }), Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
