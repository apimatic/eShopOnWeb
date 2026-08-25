using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class CreateOrderEndpointTest
{
    private static StringContent ValidRequestJson()
    {
        var request = new CreateOrderRequest
        {
            Items = new List<OrderItemLineRequest> { new() { CatalogItemId = 1, Quantity = 2 } }
        };
        return new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.PostAsync("api/orders", ValidRequestJson());

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsCreatedForAuthenticatedUser()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/orders", ValidRequestJson());

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.Content.ReadAsStringAsync()).FromJson<CreateOrderResponse>();
        Assert.IsTrue(body!.OrderId > 0);
        Assert.AreEqual("AwaitingPayment", body.Status);
        Assert.AreEqual(39.0m, body.Total); // 2 x seeded item #1 (19.5)
    }

    [TestMethod]
    public async Task ReturnsBadRequestForEmptyItems()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var request = new CreateOrderRequest { Items = new List<OrderItemLineRequest>() };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/orders", content);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
