using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SquareIntegration;

[TestClass]
public class CatalogItemPhotoEndpointTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0xFF, 0xD9];

    private static MultipartFormDataContent Photo(byte[] content, string fieldName = "photo", string fileName = "photo.png", string contentType = "image/png")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, fieldName, fileName } };
    }

    [TestMethod]
    public async Task UploadsPngAsTheSquareItemsPhoto()
    {
        await using var app = new SquareApiFactory();
        await app.SyncAsync();
        var itemId = (await app.CatalogItemIdsAsync())[0];

        var response = await app.AdminClient().PutAsync($"api/catalog-items/{itemId}/photo", Photo(Png));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(SquareApiFactory.Json);
        var imageId = body.Str("imageId");
        Assert.IsTrue(imageId!.StartsWith("SQ-IMAGE-"));
        StringAssert.StartsWith(body.Str("imageUrl"), "https://");

        var squareItem = app.Square.ItemWithSku(SquareConstants.SkuFor(itemId))!;
        Assert.AreEqual(imageId, (string?)squareItem["item_data"]!["image_ids"]![0], "the upload is the item's primary photo");
        Assert.AreEqual(body.Str("squareItemId"), (string?)squareItem["id"]);

        var upload = app.Square.RequestsTo("POST", "/v2/catalog/images").Single();
        var request = JsonNode.Parse(upload.MultipartText["request"])!;
        Assert.AreEqual((string?)squareItem["id"], (string?)request["object_id"]);
        Assert.AreEqual(true, (bool?)request["is_primary"]);
        Assert.AreEqual("IMAGE", (string?)request["image"]!["type"]);
        Assert.AreEqual("image/png", upload.MultipartContentTypes["image_file"]);
        CollectionAssert.AreEqual(Png, upload.MultipartBytes["image_file"]);

        var link = await app.WithDbAsync(db => db.SquareCatalogLinks.SingleAsync(l => l.CatalogItemId == itemId));
        Assert.AreEqual(imageId, link.SquareImageId);
    }

    [TestMethod]
    public async Task UploadsJpegRecognisedBySignature()
    {
        await using var app = new SquareApiFactory();
        await app.SyncAsync();
        var itemId = (await app.CatalogItemIdsAsync())[1];

        // Declared type and file name are not trusted; the bytes are.
        var response = await app.AdminClient().PutAsync($"api/catalog-items/{itemId}/photo", Photo(Jpeg, fileName: "photo.bin", contentType: "application/octet-stream"));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual("image/jpeg", app.Square.RequestsTo("POST", "/v2/catalog/images").Single().MultipartContentTypes["image_file"]);
    }

    [TestMethod]
    public async Task AnythingThatIsNotJpegOrPngIsRejectedWithoutCallingSquare()
    {
        await using var app = new SquareApiFactory();
        var itemId = (await app.CatalogItemIdsAsync())[0];
        var admin = app.AdminClient();

        var text = await admin.PutAsync($"api/catalog-items/{itemId}/photo", Photo(Encoding.UTF8.GetBytes("not an image"), contentType: "image/png"));
        var gif = await admin.PutAsync($"api/catalog-items/{itemId}/photo", Photo(Encoding.ASCII.GetBytes("GIF89a....."), fileName: "a.gif", contentType: "image/gif"));
        var svg = await admin.PutAsync($"api/catalog-items/{itemId}/photo", Photo(Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'/>"), fileName: "a.png"));
        var json = await admin.PutAsync($"api/catalog-items/{itemId}/photo", new StringContent("{}", Encoding.UTF8, "application/json"));
        var wrongField = await admin.PutAsync($"api/catalog-items/{itemId}/photo", Photo(Png, fieldName: "image"));

        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, text.StatusCode);
        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, gif.StatusCode);
        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, svg.StatusCode);
        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, json.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, wrongField.StatusCode);
        Assert.AreEqual(0, app.Square.Requests.Count, "nothing reached Square");
    }

    [TestMethod]
    public async Task PhotoOverFiveMegabytesIsRejectedWithoutCallingSquare()
    {
        await using var app = new SquareApiFactory();
        var itemId = (await app.CatalogItemIdsAsync())[0];
        var big = new byte[SquareConstants.MaxPhotoBytes + 1];
        Png.CopyTo(big, 0);

        var response = await app.AdminClient().PutAsync($"api/catalog-items/{itemId}/photo", Photo(big));

        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.AreEqual(0, app.Square.Requests.Count);
    }

    [TestMethod]
    public async Task PhotoOfAnItemNotYetInSquareAsksForASync()
    {
        await using var app = new SquareApiFactory();
        var itemId = (await app.CatalogItemIdsAsync())[0];

        var response = await app.AdminClient().PutAsync($"api/catalog-items/{itemId}/photo", Photo(Png));

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.AreEqual(0, app.Square.RequestsTo("POST", "/v2/catalog/images").Count);
    }

    [TestMethod]
    public async Task UnknownCatalogItemIsNotFound()
    {
        await using var app = new SquareApiFactory();
        var response = await app.AdminClient().PutAsync("api/catalog-items/999999/photo", Photo(Png));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual(0, app.Square.Requests.Count);
    }

    [TestMethod]
    public async Task UploadRequiresTheAdministratorRole()
    {
        await using var app = new SquareApiFactory();
        var shopper = await app.ShopperClient().PutAsync("api/catalog-items/1/photo", Photo(Png));
        var anonymous = await app.CreateClient().PutAsync("api/catalog-items/1/photo", Photo(Png));

        Assert.AreEqual(HttpStatusCode.Forbidden, shopper.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [TestMethod]
    public async Task UnknownOutcomeIsSettledByResendWithSameKey()
    {
        await using var app = new SquareApiFactory();
        await app.SyncAsync();
        var itemId = (await app.CatalogItemIdsAsync())[0];
        app.Square.Fail("POST", "/v2/catalog/images", FakeSquare.FaultKind.DropAfterProcessing);

        var response = await app.AdminClient().PutAsync($"api/catalog-items/{itemId}/photo", Photo(Png));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var sends = app.Square.RequestsTo("POST", "/v2/catalog/images");
        Assert.AreEqual(2, sends.Count);
        Assert.AreEqual((string?)JsonNode.Parse(sends[0].MultipartText["request"])!["idempotency_key"],
            (string?)JsonNode.Parse(sends[1].MultipartText["request"])!["idempotency_key"]);
        CollectionAssert.AreEqual(Png, sends[1].MultipartBytes["image_file"], "the re-send carries the whole file again");
        var squareItem = app.Square.ItemWithSku(SquareConstants.SkuFor(itemId))!;
        Assert.AreEqual(1, squareItem["item_data"]!["image_ids"]!.AsArray().Count, "only one image was created");
    }
}
