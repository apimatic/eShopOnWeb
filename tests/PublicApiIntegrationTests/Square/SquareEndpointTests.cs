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
using System.Web;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Square;

[TestClass]
public class SquareEndpointTests
{
    private static readonly string Admin = ApiTokenHelper.GetAdminUserToken();
    private static readonly string Shopper = ApiTokenHelper.GetNormalUserToken();

    private static async Task<JsonNode> Json(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    private static MultipartFormDataContent Photo(byte[] content, string field = "photo", string type = "image/png")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new MultipartFormDataContent { { file, field, "photo.png" } };
    }

    [TestMethod]
    public async Task OperatorEndpointsRequireTheAdministratorRole()
    {
        await using var app = new SquareApiFactory();
        var requests = new Func<HttpRequestMessage>[]
        {
            () => new HttpRequestMessage(HttpMethod.Get, "api/square/connect"),
            () => new HttpRequestMessage(HttpMethod.Get, "api/square/connection"),
            () => new HttpRequestMessage(HttpMethod.Post, "api/square/catalog/sync"),
            () => new HttpRequestMessage(HttpMethod.Put, "api/catalog-items/1/photo") { Content = Photo(SquareCatalogPhotoServiceTests.Png) },
        };

        foreach (var request in requests)
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await app.ClientFor(null).SendAsync(request())).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await app.ClientFor(Shopper).SendAsync(request())).StatusCode);
        }
        Assert.AreEqual(0, app.Square.Snapshot().Count);
    }

    [TestMethod]
    public async Task TheMerchantConnectsThroughSignInAndCallback()
    {
        await using var app = new SquareApiFactory();
        var admin = app.ClientFor(Admin);

        var before = await Json(await admin.GetAsync("api/square/connection"));
        Assert.AreEqual("accessToken", (string?)before["connectionType"]);

        var connect = await Json(await admin.GetAsync("api/square/connect"));
        var signInUrl = new Uri((string)connect["signInUrl"]!);
        var state = HttpUtility.ParseQueryString(signInUrl.Query)["state"];
        Assert.AreEqual(SquareHarness.ApplicationId, HttpUtility.ParseQueryString(signInUrl.Query)["client_id"]);

        // The merchant's browser comes back without any token.
        var callback = await app.ClientFor(null).GetAsync($"api/square/callback?code=granted-code&state={Uri.EscapeDataString(state!)}");
        Assert.AreEqual(HttpStatusCode.OK, callback.StatusCode);
        StringAssert.Contains(await callback.Content.ReadAsStringAsync(), "Fake Coffee");

        var after = await Json(await admin.GetAsync("api/square/connection"));
        Assert.IsTrue(after["connected"]!.GetValue<bool>());
        Assert.AreEqual("oauth", (string?)after["connectionType"]);
        Assert.AreEqual(app.Square.MerchantId, (string?)after["merchantId"]);
        Assert.AreEqual(app.Square.BusinessName, (string?)after["businessName"]);
    }

    [TestMethod]
    public async Task ACallbackTheShopDidNotStartIsRefused()
    {
        await using var app = new SquareApiFactory();

        var response = await app.ClientFor(null).GetAsync("api/square/callback?code=stolen&state=made-up");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, app.Square.RequestsTo(HttpMethod.Post, "/oauth2/token").Count());
        var connection = await Json(await app.ClientFor(Admin).GetAsync("api/square/connection"));
        Assert.AreEqual("accessToken", (string?)connection["connectionType"]);
    }

    [TestMethod]
    public async Task CatalogSyncReportsCreatedUpdatedAndUnchanged()
    {
        await using var app = new SquareApiFactory();
        var admin = app.ClientFor(Admin);

        var first = await Json(await admin.PostAsync("api/square/catalog/sync", null));
        var second = await Json(await admin.PostAsync("api/square/catalog/sync", null));

        Assert.AreEqual(12, first["created"]!.GetValue<int>(), "the 12 seeded catalog items");
        Assert.AreEqual(0, second["created"]!.GetValue<int>());
        Assert.AreEqual(0, second["updated"]!.GetValue<int>());
        Assert.AreEqual(12, second["unchanged"]!.GetValue<int>());
        Assert.AreEqual(12, app.Square.ItemsCreatedByShop().Count());
    }

    [TestMethod]
    public async Task APhotoIsUploadedToTheProductsSquareItem()
    {
        await using var app = new SquareApiFactory();

        var response = await app.ClientFor(Admin).PutAsync("api/catalog-items/1/photo", Photo(SquareCatalogPhotoServiceTests.Png));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        StringAssert.StartsWith((string?)body["imageId"], "IMAGE_");
        StringAssert.StartsWith((string?)body["imageUrl"], "https://images.fake-square.test/");
    }

    [TestMethod]
    public async Task AnythingButAJpegOrPngIsRejectedWithoutCallingSquare()
    {
        await using var app = new SquareApiFactory();
        var admin = app.ClientFor(Admin);

        var notAnImage = await admin.PutAsync("api/catalog-items/1/photo", Photo(Encoding.UTF8.GetBytes("<html>hi</html>")));
        var gif = await admin.PutAsync("api/catalog-items/1/photo", Photo(Encoding.ASCII.GetBytes("GIF89a\x01\x00"), type: "image/gif"));
        var wrongField = await admin.PutAsync("api/catalog-items/1/photo", Photo(SquareCatalogPhotoServiceTests.Png, field: "image"));
        var notMultipart = await admin.PutAsync("api/catalog-items/1/photo", new ByteArrayContent(SquareCatalogPhotoServiceTests.Png));
        var tooLarge = new byte[5 * 1024 * 1024 + 1];
        SquareCatalogPhotoServiceTests.Png.CopyTo(tooLarge, 0);
        var oversized = await admin.PutAsync("api/catalog-items/1/photo", Photo(tooLarge));

        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, notAnImage.StatusCode);
        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, gif.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, wrongField.StatusCode);
        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, notMultipart.StatusCode);
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.AreEqual(0, app.Square.Snapshot().Count, "Square is never called for a rejected upload");
    }

    [TestMethod]
    public async Task APhotoForAnUnknownProductIsNotFound()
    {
        await using var app = new SquareApiFactory();
        var response = await app.ClientFor(Admin).PutAsync("api/catalog-items/99999/photo", Photo(SquareCatalogPhotoServiceTests.Png));
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task AShopperPlacesAnOrderAndReadsTheGiftMessageBackFromSquare()
    {
        await using var app = new SquareApiFactory();
        var shopper = app.ClientFor(Shopper);

        var placed = await shopper.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 }, new { catalogItemId = 2, quantity = 1 } },
            giftMessage = "Happy birthday!",
        });
        Assert.AreEqual(HttpStatusCode.Created, placed.StatusCode);
        var created = await Json(placed);
        var orderId = created["orderId"]!.GetValue<int>();
        var squareOrderId = (string)created["squareOrderId"]!;
        Assert.IsTrue(created["giftMessageSaved"]!.GetValue<bool>());

        app.Square.StaffEditsGiftMessage(squareOrderId, "Happy birthday! (wrapped in blue)");
        var mine = await Json(await shopper.GetAsync($"api/my-orders/{orderId}"));

        Assert.AreEqual(squareOrderId, (string?)mine["squareOrderId"]);
        Assert.AreEqual("Happy birthday! (wrapped in blue)", (string?)mine["giftMessage"]);
        Assert.AreEqual(2, mine["items"]!.AsArray().Count);
        Assert.AreEqual(app.Square.LocationId, (string?)app.Square.Orders[squareOrderId]["location_id"]);
    }

    [TestMethod]
    public async Task OneShopperCannotSeeAnothersOrder()
    {
        await using var app = new SquareApiFactory();
        var placed = await Json(await app.ClientFor(Shopper).PostAsJsonAsync("api/orders",
            new { items = new[] { new { catalogItemId = 1, quantity = 1 } }, giftMessage = "private" }));
        var orderId = placed["orderId"]!.GetValue<int>();

        var other = await app.ClientFor(Admin).GetAsync($"api/my-orders/{orderId}");
        var anonymous = await app.ClientFor(null).GetAsync($"api/my-orders/{orderId}");

        Assert.AreEqual(HttpStatusCode.NotFound, other.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [TestMethod]
    public async Task InvalidOrdersAreRejected()
    {
        await using var app = new SquareApiFactory();
        var shopper = app.ClientFor(Shopper);

        var tooLong = await shopper.PostAsJsonAsync("api/orders",
            new { items = new[] { new { catalogItemId = 1, quantity = 1 } }, giftMessage = new string('x', 201) });
        var noItems = await shopper.PostAsJsonAsync("api/orders", new { items = Array.Empty<object>() });
        var badQuantity = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 0 } } });
        var unknownItem = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 424242, quantity = 1 } } });
        var anonymous = await app.ClientFor(null).PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });

        Assert.AreEqual(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, noItems.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, badQuantity.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, unknownItem.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(0, app.Square.RequestsTo(HttpMethod.Post, "/v2/orders").Count());
    }

    [TestMethod]
    public async Task AGiftMessageOfExactly200CharactersIsAccepted()
    {
        await using var app = new SquareApiFactory();
        var response = await app.ClientFor(Shopper).PostAsJsonAsync("api/orders",
            new { items = new[] { new { catalogItemId = 1, quantity = 1 } }, giftMessage = new string('x', 200) });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task SquareOutagesSurfaceAsGatewayErrorsWithoutLeakingDetails()
    {
        await using var app = new SquareApiFactory();
        app.Square.Intercept = _ => FakeSquare.Error(HttpStatusCode.InternalServerError, "INTERNAL_SERVER_ERROR", "API_ERROR");

        var response = await app.ClientFor(Admin).PostAsync("api/square/catalog/sync", null);

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(text.Contains("squareupsandbox"), "no SDK call details on the wire");
        Assert.IsFalse(text.Contains(SquareHarness.ConfiguredToken));
    }
}
