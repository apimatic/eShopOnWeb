using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.CatalogItemEndpoints;
using Microsoft.eShopWeb.PublicApi.SupplierCatalogSyncEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SupplierCatalogSyncEndpoints;

[TestClass]
public class SupplierCatalogSyncEndpointsTest
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static HttpClient CreateAdminClient(SupplierSyncApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
        return client;
    }

    [TestMethod]
    public async Task FullFlow_ImportsProductsIntoCatalog_AndReportsFoundVersusImported()
    {
        using var factory = new SupplierSyncApiFactory();
        var client = CreateAdminClient(factory);

        var supplierId = await RegisterSupplierAsync(client);
        var syncId = await StartSyncAsync(client, supplierId);

        var status = await PollUntilTerminalAsync(client, syncId);

        // Three products are found; the one without a price is not imported -> partial.
        Assert.AreEqual("PartiallyCompleted", status.Status);
        Assert.AreEqual(3, status.ItemsFound);
        Assert.AreEqual(2, status.ItemsImported);

        // Imported items are visible through the existing catalog listing endpoint.
        var names = await GetCatalogItemNamesAsync(client);
        CollectionAssert.Contains(names, FakeSupplierProductScraper.ProductAName);
        CollectionAssert.Contains(names, FakeSupplierProductScraper.ProductBName);
        CollectionAssert.DoesNotContain(names, FakeSupplierProductScraper.ProductCName);
    }

    [TestMethod]
    public async Task ReSync_UpdatesSameItems_WithoutDuplicating()
    {
        using var factory = new SupplierSyncApiFactory();
        var client = CreateAdminClient(factory);

        var supplierId = await RegisterSupplierAsync(client);

        var firstSyncId = await StartSyncAsync(client, supplierId);
        await PollUntilTerminalAsync(client, firstSyncId);
        var countAfterFirst = (await GetCatalogItemNamesAsync(client)).Count;

        var secondSyncId = await StartSyncAsync(client, supplierId);
        var secondStatus = await PollUntilTerminalAsync(client, secondSyncId);
        var countAfterSecond = (await GetCatalogItemNamesAsync(client)).Count;

        Assert.AreEqual(2, secondStatus.ItemsImported);
        Assert.AreEqual(countAfterFirst, countAfterSecond, "Re-syncing must not create duplicate catalog items.");
    }

    [TestMethod]
    public async Task Endpoints_RequireAdministratorRole()
    {
        using var factory = new SupplierSyncApiFactory();

        var anonymous = factory.CreateClient();
        var anonResponse = await anonymous.PostAsync("api/catalog/suppliers", JsonBody(
            new RegisterSupplierRequest { Name = "x", ProductListingUrl = "https://example.com/" }));
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonResponse.StatusCode);

        var normalUser = factory.CreateClient();
        normalUser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var forbiddenResponse = await normalUser.PostAsync("api/catalog/suppliers", JsonBody(
            new RegisterSupplierRequest { Name = "x", ProductListingUrl = "https://example.com/" }));
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    [TestMethod]
    public async Task StartSync_ForUnknownSupplier_ReturnsNotFound()
    {
        using var factory = new SupplierSyncApiFactory();
        var client = CreateAdminClient(factory);

        var response = await client.PostAsync($"api/catalog/suppliers/{Guid.NewGuid()}/sync", content: null);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<Guid> RegisterSupplierAsync(HttpClient client)
    {
        var response = await client.PostAsync("api/catalog/suppliers", JsonBody(new RegisterSupplierRequest
        {
            Name = "Fixture Supplier",
            ProductListingUrl = "https://supplier.example/listing"
        }));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var body = await Deserialize<RegisterSupplierResponse>(response);
        Assert.AreNotEqual(Guid.Empty, body.SupplierId);
        return body.SupplierId;
    }

    private static async Task<Guid> StartSyncAsync(HttpClient client, Guid supplierId)
    {
        var response = await client.PostAsync($"api/catalog/suppliers/{supplierId}/sync", content: null);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);

        var body = await Deserialize<StartSupplierSyncResponse>(response);
        Assert.AreNotEqual(Guid.Empty, body.SyncId);
        return body.SyncId;
    }

    private static async Task<GetSupplierSyncResponse> PollUntilTerminalAsync(HttpClient client, Guid syncId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var response = await client.GetAsync($"api/catalog/syncs/{syncId}");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            var status = await Deserialize<GetSupplierSyncResponse>(response);
            if (status.Status is "Completed" or "PartiallyCompleted" or "Failed")
                return status;

            await Task.Delay(100);
        }

        Assert.Fail("Sync did not reach a terminal status in time.");
        throw new InvalidOperationException();
    }

    private static async Task<System.Collections.Generic.List<string>> GetCatalogItemNamesAsync(HttpClient client)
    {
        var response = await client.GetAsync("api/catalog-items?pageSize=100&pageIndex=0");
        response.EnsureSuccessStatusCode();
        var listing = await Deserialize<ListPagedCatalogItemResponse>(response);
        return listing.CatalogItems.Select(i => i.Name).ToList();
    }

    private static StringContent JsonBody(object value) =>
        new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");

    private static async Task<T> Deserialize<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var value = JsonSerializer.Deserialize<T>(json, JsonOptions);
        Assert.IsNotNull(value);
        return value!;
    }
}
