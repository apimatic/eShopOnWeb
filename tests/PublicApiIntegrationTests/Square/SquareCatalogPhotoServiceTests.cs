using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Square;

[TestClass]
public class SquareCatalogPhotoServiceTests
{
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 5, 6, 7, 8];

    private static Task<SquarePhotoResult?> Upload(SquareHarness h, int id, byte[] image, SquareImageFormat format) =>
        h.Run<SquareCatalogPhotoService, SquarePhotoResult?>(s => s.UploadAsync(id, image, format, default));

    [TestMethod]
    public void OnlyJpegAndPngSignaturesAreAccepted()
    {
        Assert.AreEqual(SquareImageFormat.Png, SquareImageValidation.Detect(Png));
        Assert.AreEqual(SquareImageFormat.Jpeg, SquareImageValidation.Detect(Jpeg));
        Assert.IsNull(SquareImageValidation.Detect(Encoding.ASCII.GetBytes("GIF89a....")));
        Assert.IsNull(SquareImageValidation.Detect(Encoding.ASCII.GetBytes("<svg xmlns=...")));
        Assert.IsNull(SquareImageValidation.Detect(Array.Empty<byte>()));
    }

    [TestMethod]
    public async Task UploadBecomesThePrimaryImageOfTheProductsSquareItem()
    {
        await using var h = new SquareHarness();
        var item = (await h.SeedCatalogAsync(("Mug", 8.5m))).Single();

        var result = await Upload(h, item.Id, Png, SquareImageFormat.Png);

        Assert.IsNotNull(result);
        var squareItem = h.Square.ItemsCreatedByShop().Single();
        Assert.AreEqual(squareItem["id"]!.GetValue<string>(), result!.SquareItemId, "the item was synced first");
        Assert.AreEqual(result.ImageId, (string?)squareItem["item_data"]!["image_ids"]![0]);
        Assert.AreEqual($"https://images.fake-square.test/{result.ImageId}.img", result.ImageUrl);

        var upload = h.Square.RequestsTo(HttpMethod.Post, "/v2/catalog/images").Single();
        var json = JsonNode.Parse(Encoding.UTF8.GetString(upload.Parts["request"]))!;
        Assert.AreEqual(result.SquareItemId, (string?)json["object_id"]);
        Assert.IsTrue(json["is_primary"]!.GetValue<bool>());
        Assert.AreEqual("IMAGE", (string?)json["image"]!["type"]);
        CollectionAssert.AreEqual(Png, upload.Parts["image_file"]);
        Assert.AreEqual("image/png", upload.PartContentTypes["image_file"]);
    }

    [TestMethod]
    public async Task UploadingTheSamePhotoAgainChangesNothing()
    {
        await using var h = new SquareHarness();
        var item = (await h.SeedCatalogAsync(("Mug", 8.5m))).Single();
        var first = await Upload(h, item.Id, Jpeg, SquareImageFormat.Jpeg);

        var second = await Upload(h, item.Id, Jpeg, SquareImageFormat.Jpeg);

        Assert.AreEqual(first!.ImageId, second!.ImageId);
        Assert.AreEqual(1, h.Square.RequestsTo(HttpMethod.Post, "/v2/catalog/images").Count());
    }

    [TestMethod]
    public async Task ANewPhotoReplacesThePrimaryImage()
    {
        await using var h = new SquareHarness();
        var item = (await h.SeedCatalogAsync(("Mug", 8.5m))).Single();
        var first = await Upload(h, item.Id, Jpeg, SquareImageFormat.Jpeg);

        var second = await Upload(h, item.Id, Png, SquareImageFormat.Png);

        Assert.AreNotEqual(first!.ImageId, second!.ImageId);
        Assert.AreEqual(second.ImageId, (string?)h.Square.ItemsCreatedByShop().Single()["item_data"]!["image_ids"]![0]);
    }

    [TestMethod]
    public async Task AnUnknownCatalogItemReturnsNullWithoutCallingSquare()
    {
        await using var h = new SquareHarness();
        Assert.IsNull(await Upload(h, 999, Png, SquareImageFormat.Png));
        Assert.AreEqual(0, h.Square.Snapshot().Count);
    }

    [TestMethod]
    public async Task ConnectionFailureAfterUploadIsSettledByReadingItem()
    {
        await using var h = new SquareHarness();
        var item = (await h.SeedCatalogAsync(("Mug", 8.5m))).Single();
        h.Square.LoseResponse = r => r.Path == "/v2/catalog/images"; // the image lands, the reply is lost

        var result = await Upload(h, item.Id, Png, SquareImageFormat.Png);

        Assert.IsNotNull(result);
        Assert.AreEqual(1, h.Square.RequestsTo(HttpMethod.Post, "/v2/catalog/images").Count(), "settled by reading, not by uploading again");
        Assert.AreEqual(result!.ImageId, (string?)h.Square.ItemsCreatedByShop().Single()["item_data"]!["image_ids"]![0]);
        var link = await h.Db(db => db.SquareCatalogLinks.SingleAsync());
        Assert.AreEqual(SquarePhotoState.Done, link.PhotoState);
    }

    [TestMethod]
    public async Task AnUploadThatNeverLandedIsReportedAsUnknownAndRetriedWithTheSameKey()
    {
        await using var h = new SquareHarness();
        var item = (await h.SeedCatalogAsync(("Mug", 8.5m))).Single();
        h.Square.Intercept = r => r.Path == "/v2/catalog/images" ? throw new HttpRequestException("connection refused") : null;

        var ex = await Assert.ThrowsExceptionAsync<SquareIntegrationException>(() => Upload(h, item.Id, Png, SquareImageFormat.Png));
        Assert.AreEqual(SquareFailureKind.OutcomeUnknown, ex.Kind);

        h.Square.Intercept = null;
        var result = await Upload(h, item.Id, Png, SquareImageFormat.Png);

        var uploads = h.Square.RequestsTo(HttpMethod.Post, "/v2/catalog/images").ToList();
        Assert.AreEqual(2, uploads.Count);
        string Key(RecordedRequest r) => (string)JsonNode.Parse(Encoding.UTF8.GetString(r.Parts["request"]))!["idempotency_key"]!;
        Assert.AreEqual(Key(uploads[0]), Key(uploads[1]));
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public async Task ARejectedUploadReleasesTheClaim()
    {
        await using var h = new SquareHarness();
        var item = (await h.SeedCatalogAsync(("Mug", 8.5m))).Single();
        h.Square.Intercept = r => r.Path == "/v2/catalog/images"
            ? FakeSquare.Error(HttpStatusCode.BadRequest, "INVALID_CONTENT_TYPE", "INVALID_REQUEST_ERROR")
            : null;

        var ex = await Assert.ThrowsExceptionAsync<SquareIntegrationException>(() => Upload(h, item.Id, Png, SquareImageFormat.Png));

        Assert.AreEqual(SquareFailureKind.Rejected, ex.Kind);
        var link = await h.Db(db => db.SquareCatalogLinks.SingleAsync());
        Assert.IsNull(link.PhotoState);
    }
}
