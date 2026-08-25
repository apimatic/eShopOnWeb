using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class PlaceOrderEndpointTest
{
    [TestMethod]
    public async Task ReturnsUnauthorizedWithNoToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.PostAsync("api/orders", JsonBody());

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PlacesOrderAndReturnsOrderIdForAuthenticatedShopper()
    {
        var token = ApiTokenHelper.GetNormalUserToken();
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync("api/orders", JsonBody());
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var result = body.FromJson<PlaceOrderResponse>();

        Assert.IsNotNull(result);
        Assert.IsTrue(result!.OrderId > 0);
        Assert.AreEqual("AwaitingPayment", result.Order.Status);
        Assert.AreEqual(1, result.Order.Items.Count);
    }

    [TestMethod]
    public async Task UnknownCatalogItemReturnsNotFound()
    {
        var token = ApiTokenHelper.GetNormalUserToken();
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new { Items = new[] { new { CatalogItemId = 999999, Quantity = 1 } } };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/orders", content);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static StringContent JsonBody()
    {
        var request = new { Items = new[] { new { CatalogItemId = 1, Quantity = 1 } } };
        return new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
    }
}
