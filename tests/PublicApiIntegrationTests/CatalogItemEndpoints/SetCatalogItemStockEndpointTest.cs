using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.CatalogItemEndpoints;

[TestClass]
public class SetCatalogItemStockEndpointTest
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static StringContent QuantityBody(int quantity) =>
        new(JsonSerializer.Serialize(new { quantity }), Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task ReturnsForbiddenGivenNormalUserToken()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PutAsync("api/catalog-items/1/stock", QuantityBody(5));

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task SetsStockGivenAdminToken()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.PutAsync("api/catalog-items/2/stock", QuantityBody(17));

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(2, doc.RootElement.GetProperty("catalogItemId").GetInt32());
        Assert.AreEqual(17, doc.RootElement.GetProperty("quantity").GetInt32());
        Assert.AreEqual("ESHOP-2", doc.RootElement.GetProperty("sku").GetString());
    }

    [TestMethod]
    public async Task RejectsNegativeQuantity()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.PutAsync("api/catalog-items/1/stock", QuantityBody(-1));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsNotFoundForUnknownItem()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.PutAsync("api/catalog-items/999999/stock", QuantityBody(1));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
