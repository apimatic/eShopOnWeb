using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.DigitalFileEndpoints;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.DigitalFiles;

/// <summary>
/// Drives the digital download flows through the PublicApi endpoints, with Box replaced by
/// <see cref="FakeDigitalFileStorage"/>. Each test uses its own catalog items because links are stored per item.
/// </summary>
[TestClass]
public class DigitalDownloadEndpointsTest
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static FakeDigitalFileStorage Storage => ProgramTest.Storage;

    [TestMethod]
    public async Task ListingFilesReturnsIdNameAndSizeForAdministrators()
    {
        Storage.AddFile("list-1", "guide.pdf", new byte[] { 1, 2, 3 });

        var response = await Client(ApiTokenHelper.GetAdminUserToken()).GetAsync("api/digital-files");

        response.EnsureSuccessStatusCode();
        var model = await response.Content.ReadFromJsonAsync<ListDigitalFilesResponse>(Json);
        var file = model!.Files.Single(f => f.Id == "list-1");
        Assert.AreEqual("guide.pdf", file.Name);
        Assert.AreEqual(3, file.Size);
        Assert.AreEqual("eshop-digital-products", model.FolderName);
        Assert.IsFalse(model.IsTruncated);
    }

    [TestMethod]
    public async Task ListingFilesIsForbiddenForShoppersAndAnonymousCallers()
    {
        var shopper = await Client(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/digital-files");
        var anonymous = await ProgramTest.NewClient.GetAsync("api/digital-files");

        Assert.AreEqual(HttpStatusCode.Forbidden, shopper.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [TestMethod]
    public async Task ListingFilesReportsStorageOutageAsBadGateway()
    {
        Storage.ListFailure = new DigitalFileStorageException("Box could not be reached.", DigitalFileStorageFailure.Unreachable);
        try
        {
            var response = await Client(ApiTokenHelper.GetAdminUserToken()).GetAsync("api/digital-files");

            Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Box could not be reached.");
        }
        finally
        {
            Storage.ListFailure = null;
        }
    }

    [TestMethod]
    public async Task LinkingReturnsTheFileNameAndSize()
    {
        Storage.AddFile("link-ok", "poster.png", new byte[10]);

        var response = await Link(ApiTokenHelper.GetAdminUserToken(), 2, "link-ok");

        response.EnsureSuccessStatusCode();
        var model = await response.Content.ReadFromJsonAsync<LinkDigitalFileResponse>(Json);
        Assert.AreEqual(2, model!.CatalogItemId);
        Assert.AreEqual("link-ok", model.FileId);
        Assert.AreEqual("poster.png", model.FileName);
        Assert.AreEqual(10, model.Size);
    }

    [TestMethod]
    public async Task LinkingRefusesAFileThatIsNotInTheBoxFolder()
    {
        var response = await Link(ApiTokenHelper.GetAdminUserToken(), 2, "does-not-exist");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task LinkingRefusesAnUnknownCatalogItem()
    {
        Storage.AddFile("link-unknown-item", "a.pdf", new byte[1]);

        var response = await Link(ApiTokenHelper.GetAdminUserToken(), 9999, "link-unknown-item");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task LinkingIsForbiddenForShoppers()
    {
        Storage.AddFile("link-forbidden", "a.pdf", new byte[1]);

        var response = await Link(ApiTokenHelper.GetNormalUserToken(), 2, "link-forbidden");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task PlacingAnOrderReturnsItsId()
    {
        var orderId = await PlaceOrder(ApiTokenHelper.GetNormalUserToken(), (1, 2), (3, 1));

        Assert.IsTrue(orderId > 0);
    }

    [TestMethod]
    public async Task PlacingAnOrderValidatesItems()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        var empty = await client.PostAsJsonAsync("api/orders", new { items = Array.Empty<object>() });
        var unknownItem = await client.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 9999, quantity = 1 } } });
        var badQuantity = await client.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 0 } } });
        var anonymous = await ProgramTest.NewClient.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });

        Assert.AreEqual(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, unknownItem.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, badQuantity.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [TestMethod]
    public async Task ShopperDownloadsThePurchasedFileWithItsNameAndType()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7 test content");
        Storage.AddFile("dl-ok", "eshop guide ü.pdf", bytes);
        (await Link(ApiTokenHelper.GetAdminUserToken(), 3, "dl-ok")).EnsureSuccessStatusCode();
        var shopper = ApiTokenHelper.GetNormalUserToken();
        var orderId = await PlaceOrder(shopper, (3, 1));

        var response = await Client(shopper).GetAsync($"api/orders/{orderId}/downloads/3");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        CollectionAssert.AreEqual(bytes, await response.Content.ReadAsByteArrayAsync());
        // Storage reported application/octet-stream, so the type comes from the file extension.
        Assert.AreEqual("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.AreEqual(bytes.Length, response.Content.Headers.ContentLength);
        Assert.AreEqual("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.AreEqual("eshop guide ü.pdf", response.Content.Headers.ContentDisposition.FileNameStar);
    }

    [TestMethod]
    public async Task AnotherShopperCannotDownloadThePurchase()
    {
        Storage.AddFile("dl-other", "a.pdf", new byte[5]);
        (await Link(ApiTokenHelper.GetAdminUserToken(), 4, "dl-other")).EnsureSuccessStatusCode();
        var orderId = await PlaceOrder(ApiTokenHelper.GetNormalUserToken(), (4, 1));

        var otherShopper = await Client(ApiTokenHelper.GetAdminUserToken()).GetAsync($"api/orders/{orderId}/downloads/4");
        var anonymous = await ProgramTest.NewClient.GetAsync($"api/orders/{orderId}/downloads/4");

        Assert.AreEqual(HttpStatusCode.NotFound, otherShopper.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [TestMethod]
    public async Task ItemsNotInTheOrderOrWithoutAFileAreRefused()
    {
        Storage.AddFile("dl-not-ordered", "a.pdf", new byte[5]);
        (await Link(ApiTokenHelper.GetAdminUserToken(), 5, "dl-not-ordered")).EnsureSuccessStatusCode();
        var shopper = ApiTokenHelper.GetNormalUserToken();
        // Item 6 is ordered but never linked; item 5 is linked but not ordered.
        var orderId = await PlaceOrder(shopper, (6, 1));

        var notOrdered = await Client(shopper).GetAsync($"api/orders/{orderId}/downloads/5");
        var noFile = await Client(shopper).GetAsync($"api/orders/{orderId}/downloads/6");
        var noOrder = await Client(shopper).GetAsync("api/orders/999999/downloads/5");

        Assert.AreEqual(HttpStatusCode.NotFound, notOrdered.StatusCode);
        StringAssert.Contains(await notOrdered.Content.ReadAsStringAsync(), "not part of order");
        Assert.AreEqual(HttpStatusCode.NotFound, noFile.StatusCode);
        StringAssert.Contains(await noFile.Content.ReadAsStringAsync(), "no downloadable file");
        Assert.AreEqual(HttpStatusCode.NotFound, noOrder.StatusCode);
    }

    [TestMethod]
    public async Task FileRemovedFromBoxAfterLinkingIsReportedAsUnavailable()
    {
        Storage.AddFile("dl-gone", "a.pdf", new byte[5]);
        (await Link(ApiTokenHelper.GetAdminUserToken(), 7, "dl-gone")).EnsureSuccessStatusCode();
        var shopper = ApiTokenHelper.GetNormalUserToken();
        var orderId = await PlaceOrder(shopper, (7, 1));
        Storage.FailOpen("dl-gone", new DigitalFileNotFoundException("dl-gone"));

        var response = await Client(shopper).GetAsync($"api/orders/{orderId}/downloads/7");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "no longer available");
    }

    [TestMethod]
    public async Task StorageFailureWhenOpeningIsReportedAsBadGateway()
    {
        Storage.AddFile("dl-unauthorized", "a.pdf", new byte[5]);
        (await Link(ApiTokenHelper.GetAdminUserToken(), 8, "dl-unauthorized")).EnsureSuccessStatusCode();
        var shopper = ApiTokenHelper.GetNormalUserToken();
        var orderId = await PlaceOrder(shopper, (8, 1));
        Storage.FailOpen("dl-unauthorized",
            new DigitalFileStorageException("Box rejected the shop's credentials.", DigitalFileStorageFailure.Unauthorized, 401));

        var response = await Client(shopper).GetAsync($"api/orders/{orderId}/downloads/8");

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [TestMethod]
    public async Task BoxStallingBeforeTheFirstByteYieldsGatewayTimeoutNotAFile()
    {
        var stalled = new StallingStream(Array.Empty<byte>());
        Storage.AddFile("dl-stall-start", "a.pdf", 100,
            () => new DigitalFileContent(stalled, "a.pdf", "application/pdf", 100));
        (await Link(ApiTokenHelper.GetAdminUserToken(), 9, "dl-stall-start")).EnsureSuccessStatusCode();
        var shopper = ApiTokenHelper.GetNormalUserToken();
        var orderId = await PlaceOrder(shopper, (9, 1));

        var response = await Client(shopper).GetAsync($"api/orders/{orderId}/downloads/9");

        Assert.AreEqual(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.IsTrue(stalled.Disposed, "the Box stream must be released");
    }

    [TestMethod]
    public async Task BoxStallingMidDownloadAbortsTheResponseInsteadOfEndingIt()
    {
        var stalled = new StallingStream(new byte[1000]);
        Storage.AddFile("dl-stall-mid", "a.pdf", 5000,
            () => new DigitalFileContent(stalled, "a.pdf", "application/pdf", 5000));
        (await Link(ApiTokenHelper.GetAdminUserToken(), 10, "dl-stall-mid")).EnsureSuccessStatusCode();
        var shopper = ApiTokenHelper.GetNormalUserToken();
        var orderId = await PlaceOrder(shopper, (10, 1));

        var received = await TryDownloadCompletely(Client(shopper), $"api/orders/{orderId}/downloads/10");

        Assert.IsNull(received, "a partial file must not be delivered as a complete response");
        Assert.IsTrue(stalled.Disposed, "the Box stream must be released");
    }

    [TestMethod]
    public async Task StallOnARealSocketIsDetectedAndTheResponseAborted()
    {
        await using var server = new StallingHttpServer(announcedLength: 50_000, sentBytes: 2_000);
        using var boxLikeClient = new HttpClient();
        Storage.AddFile("dl-stall-socket", "a.pdf", 50_000, () =>
        {
            var upstream = server.OpenAsync(boxLikeClient).GetAwaiter().GetResult();
            return new DigitalFileContent(upstream.Content.ReadAsStream(), "a.pdf", "application/pdf",
                upstream.Content.Headers.ContentLength);
        });
        (await Link(ApiTokenHelper.GetAdminUserToken(), 1, "dl-stall-socket")).EnsureSuccessStatusCode();
        var shopper = ApiTokenHelper.GetNormalUserToken();
        var orderId = await PlaceOrder(shopper, (1, 1));
        var started = DateTime.UtcNow;

        var received = await TryDownloadCompletely(Client(shopper), $"api/orders/{orderId}/downloads/1");

        Assert.IsNull(received, "a partial file must not be delivered as a complete response");
        Assert.IsTrue(DateTime.UtcNow - started < TimeSpan.FromSeconds(20), "the stall watchdog must end the download");
    }

    [TestMethod]
    public async Task BoxEndingEarlyAbortsTheResponseInsteadOfEndingIt()
    {
        // Box announced 5000 bytes but the stream ends after 1000.
        Storage.AddFile("dl-short", "a.pdf", 5000,
            () => new DigitalFileContent(new MemoryStream(new byte[1000]), "a.pdf", "application/pdf", 5000));
        (await Link(ApiTokenHelper.GetAdminUserToken(), 11, "dl-short")).EnsureSuccessStatusCode();
        var shopper = ApiTokenHelper.GetNormalUserToken();
        var orderId = await PlaceOrder(shopper, (11, 1));

        var received = await TryDownloadCompletely(Client(shopper), $"api/orders/{orderId}/downloads/11");

        Assert.IsNull(received, "a partial file must not be delivered as a complete response");
    }

    /// <returns>The body when the response completed normally; null when the transfer failed.</returns>
    private static async Task<byte[]?> TryDownloadCompletely(HttpClient client, string url)
    {
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return null;
        }
    }

    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> Link(string token, int catalogItemId, string fileId) =>
        Client(token).PutAsJsonAsync($"api/catalog-items/{catalogItemId}/digital-file", new { fileId });

    private static async Task<int> PlaceOrder(string token, params (int CatalogItemId, int Quantity)[] items)
    {
        var response = await Client(token).PostAsJsonAsync("api/orders",
            new { items = items.Select(i => new { catalogItemId = i.CatalogItemId, quantity = i.Quantity }) });
        response.EnsureSuccessStatusCode();
        var model = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(Json);
        return model!.OrderId;
    }
}
