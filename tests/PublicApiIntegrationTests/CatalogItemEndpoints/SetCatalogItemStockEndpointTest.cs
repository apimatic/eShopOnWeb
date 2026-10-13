using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.CatalogItemEndpoints;

[TestClass]
public class SetCatalogItemStockEndpointTest
{
    private static HttpClient AdminClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
        return client;
    }

    private static async Task<int> CreateItemAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("api/catalog-items", new
        {
            catalogBrandId = 1,
            catalogTypeId = 2,
            description = "stock test",
            name = "stock test " + Guid.NewGuid(),
            price = 1.23m,
        });
        response.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return json.GetProperty("catalogItem").GetProperty("id").GetInt32();
    }

    [TestMethod]
    public async Task Admin_SetsEshopStockLevel()
    {
        var admin = AdminClient();
        var id = await CreateItemAsync(admin);

        var response = await admin.PutAsJsonAsync($"api/catalog-items/{id}/stock", new { quantity = 42 });
        response.EnsureSuccessStatusCode();

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual(id, json.GetProperty("catalogItemId").GetInt32());
        Assert.AreEqual(42, json.GetProperty("availableStock").GetInt32());
    }

    [TestMethod]
    public async Task NonAdmin_IsForbidden()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PutAsJsonAsync("api/catalog-items/1/stock", new { quantity = 1 });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task NegativeQuantity_IsRejected()
    {
        var admin = AdminClient();
        var id = await CreateItemAsync(admin);

        var response = await admin.PutAsJsonAsync($"api/catalog-items/{id}/stock", new { quantity = -5 });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UnknownItem_IsNotFound()
    {
        var response = await AdminClient().PutAsJsonAsync("api/catalog-items/987654321/stock", new { quantity = 1 });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
