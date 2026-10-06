using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SquareIntegration;

[TestClass]
public class OrderEndpointTests
{
    private const string GiftKey = SquareConstants.GiftMessageAttributeKey;

    private static object OrderBody(int[] ids, string? giftMessage = "Happy birthday, Sam!") => new
    {
        items = ids.Select((id, i) => new { catalogItemId = id, quantity = i + 1 }).ToArray(),
        giftMessage,
    };

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> PlaceAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("api/orders", body);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(SquareApiFactory.Json);
        return (response, json);
    }

    private static async Task<JsonElement> MyOrderAsync(HttpClient client, int orderId)
    {
        var response = await client.GetAsync($"api/my-orders/{orderId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>(SquareApiFactory.Json);
    }

    [TestMethod]
    public async Task PlacesTheOrderInEshopAndSquareWithTheGiftMessageOnTheSquareOrder()
    {
        await using var app = new SquareApiFactory();
        await app.SyncAsync();
        var ids = (await app.CatalogItemIdsAsync()).Take(2).ToArray();
        var shopper = app.ShopperClient();

        var (response, body) = await PlaceAsync(shopper, OrderBody(ids));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, body.ToString());
        var orderId = body.Int("orderId");
        Assert.AreEqual("synced", body.Str("squareSyncStatus"));

        // eShop's own order model holds the order, for the caller from the token.
        var order = await app.WithDbAsync(db => db.Orders.Include(o => o.OrderItems).SingleAsync(o => o.Id == orderId));
        Assert.AreEqual("demouser@microsoft.com", order.BuyerId);
        Assert.AreEqual(2, order.OrderItems.Count);

        // Square has the same items and prices, at the merchant's active location.
        var squareOrder = app.Square.Orders.Single();
        Assert.AreEqual(body.Str("squareOrderId"), (string?)squareOrder["id"]);
        Assert.AreEqual(FakeSquare.LocationId, (string?)squareOrder["location_id"]);
        Assert.AreEqual($"eshop-{orderId}", (string?)squareOrder["reference_id"]);
        var lines = squareOrder["line_items"]!.AsArray();
        Assert.AreEqual(2, lines.Count);
        foreach (var item in order.OrderItems)
        {
            var line = lines.Single(l => (string?)l!["name"] == item.ItemOrdered.ProductName)!;
            Assert.AreEqual(item.Units.ToString(), (string?)line["quantity"]);
            Assert.AreEqual((long)(item.UnitPrice * 100), (long)line["base_price_money"]!["amount"]!);
            Assert.IsNotNull((string?)line["catalog_object_id"], "synced items reference the Square variation");
        }

        Assert.AreEqual((long)(order.Total() * 100), (long)squareOrder["total_money"]!["amount"]!);

        // The gift message lives on the Square order, in the "Gift message" field staff can edit.
        var definition = app.Square.Definition(GiftKey)!;
        Assert.AreEqual("Gift message", (string?)definition["name"]);
        Assert.AreEqual("VISIBILITY_READ_WRITE_VALUES", (string?)definition["visibility"]);
        Assert.AreEqual("https://developer-production-s.squarecdn.com/schemas/v1/common.json#squareup.common.String", (string?)definition["schema"]!["$ref"]);
        Assert.AreEqual("Happy birthday, Sam!", app.Square.OrderAttributeValue((string)squareOrder["id"]!, GiftKey));

        // ...and not in eShop's database.
        var link = await app.WithDbAsync(db => db.SquareOrderLinks.AsNoTracking().SingleAsync(l => l.OrderId == orderId));
        Assert.IsFalse(JsonSerializer.Serialize(link).Contains("Happy birthday"));
    }

    [TestMethod]
    public async Task MyOrderShowsTheGiftMessageAsSquareHoldsItNow()
    {
        await using var app = new SquareApiFactory();
        var ids = (await app.CatalogItemIdsAsync()).Take(1).ToArray();
        var shopper = app.ShopperClient();
        var (_, placed) = await PlaceAsync(shopper, OrderBody(ids));
        var orderId = placed.Int("orderId");

        var before = await MyOrderAsync(shopper, orderId);
        Assert.AreEqual("Happy birthday, Sam!", before.Str("giftMessage"));
        Assert.AreEqual(placed.Str("squareOrderId"), before.Str("squareOrderId"));
        Assert.AreEqual("OPEN", before.Str("squareOrderState"));

        app.Square.StaffEditsOrderAttribute(placed.Str("squareOrderId")!, GiftKey, "Happy 30th birthday, Sam!");

        var after = await MyOrderAsync(shopper, orderId);
        Assert.AreEqual("Happy 30th birthday, Sam!", after.Str("giftMessage"));
        Assert.IsNull(after.Str("squareError"));
    }

    [TestMethod]
    public async Task ItemsNotYetInSquareAreSentWithTheirNameAndPrice()
    {
        await using var app = new SquareApiFactory();
        var ids = (await app.CatalogItemIdsAsync()).Take(2).ToArray();

        var (response, body) = await PlaceAsync(app.ShopperClient(), OrderBody(ids, giftMessage: null));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, body.ToString());
        var items = await app.WithDbAsync(db => db.CatalogItems.Where(c => ids.Contains(c.Id)).ToListAsync());
        var lines = app.Square.Orders.Single()["line_items"]!.AsArray();
        foreach (var item in items)
        {
            var line = lines.Single(l => (string?)l!["name"] == item.Name)!;
            Assert.IsNull(line["catalog_object_id"]);
            Assert.AreEqual((long)(item.Price * 100), (long)line["base_price_money"]!["amount"]!);
            Assert.AreEqual("USD", (string?)line["base_price_money"]!["currency"]);
        }

        Assert.IsNull(app.Square.Definition(GiftKey), "no gift message, no field needed");
    }

    [TestMethod]
    public async Task OneShopperCannotSeeAnothersOrder()
    {
        await using var app = new SquareApiFactory();
        var ids = (await app.CatalogItemIdsAsync()).Take(1).ToArray();
        var (_, placed) = await PlaceAsync(app.ShopperClient(), OrderBody(ids));
        var orderId = placed.Int("orderId");

        var otherShopper = app.ClientWith(ApiTokenHelperForOtherUser());
        var response = await otherShopper.GetAsync($"api/my-orders/{orderId}");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task OrdersRequireAnAuthenticatedCaller()
    {
        await using var app = new SquareApiFactory();
        var anonymous = app.CreateClient();

        var post = await anonymous.PostAsJsonAsync("api/orders", OrderBody([1]));
        var get = await anonymous.GetAsync("api/my-orders/1");

        Assert.AreEqual(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.AreEqual(0, app.Square.Requests.Count);
    }

    [TestMethod]
    public async Task InvalidOrdersAreRejectedBeforeAnythingIsWritten()
    {
        await using var app = new SquareApiFactory();
        var shopper = app.ShopperClient();
        var ids = await app.CatalogItemIdsAsync();

        var tooLong = await shopper.PostAsJsonAsync("api/orders", OrderBody([ids[0]], new string('x', 201)));
        var empty = await shopper.PostAsJsonAsync("api/orders", new { items = Array.Empty<object>() });
        var zero = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = ids[0], quantity = 0 } } });
        var unknown = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 987654, quantity = 1 } } });

        Assert.AreEqual(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.AreEqual(0, app.Square.Requests.Count);
        Assert.AreEqual(0, await app.WithDbAsync(db => db.Orders.CountAsync()));
    }

    [TestMethod]
    public async Task GiftMessageOfExactlyTwoHundredCharactersIsAccepted()
    {
        await using var app = new SquareApiFactory();
        var ids = await app.CatalogItemIdsAsync();
        var message = new string('g', 200);

        var (response, body) = await PlaceAsync(app.ShopperClient(), OrderBody([ids[0]], message));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, body.ToString());
        Assert.AreEqual(message, app.Square.OrderAttributeValue(body.Str("squareOrderId")!, GiftKey));
    }

    [TestMethod]
    public async Task NotConnectedRefusesTheOrderWithoutWritingIt()
    {
        await using var app = new SquareApiFactory(withAccessToken: false);
        var ids = await app.CatalogItemIdsAsync();

        var (response, _) = await PlaceAsync(app.ShopperClient(), OrderBody([ids[0]]));

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.AreEqual(0, await app.WithDbAsync(db => db.Orders.CountAsync()));
    }

    [TestMethod]
    public async Task SquareRejectingTheOrderRollsBackTheEshopOrder()
    {
        await using var app = new SquareApiFactory();
        var ids = await app.CatalogItemIdsAsync();
        app.Square.Fail("POST", "/v2/orders", FakeSquare.FaultKind.Respond, status: HttpStatusCode.BadRequest, code: "INVALID_VALUE");

        var (response, _) = await PlaceAsync(app.ShopperClient(), OrderBody([ids[0]], giftMessage: null));

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.AreEqual(0, await app.WithDbAsync(db => db.Orders.CountAsync()));
        Assert.AreEqual(0, await app.WithDbAsync(db => db.SquareOrderLinks.CountAsync()));
    }

    [TestMethod]
    public async Task CreateOrderUnknownOutcomeIsSettledWithSameKey()
    {
        await using var app = new SquareApiFactory();
        var ids = await app.CatalogItemIdsAsync();
        app.Square.Fail("POST", "/v2/orders", FakeSquare.FaultKind.DropAfterProcessing);

        var (response, body) = await PlaceAsync(app.ShopperClient(), OrderBody([ids[0]]));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, body.ToString());
        Assert.AreEqual(1, app.Square.Orders.Count, "the re-send did not create a second Square order");
        var sends = app.Square.RequestsTo("POST", "/v2/orders").Where(r => r.Path == "/v2/orders").ToList();
        Assert.AreEqual(2, sends.Count);
        Assert.AreEqual((string?)sends[0].Json!["idempotency_key"], (string?)sends[1].Json!["idempotency_key"]);
        Assert.AreEqual("Happy birthday, Sam!", app.Square.OrderAttributeValue(body.Str("squareOrderId")!, GiftKey));
    }

    [TestMethod]
    public async Task PendingOrderIsSettledOnRead()
    {
        await using var app = new SquareApiFactory();
        var ids = await app.CatalogItemIdsAsync();
        var shopper = app.ShopperClient();
        app.Square.Fail("POST", "/v2/orders", FakeSquare.FaultKind.DropAfterProcessing, times: 2);

        var (response, body) = await PlaceAsync(shopper, OrderBody([ids[0]]));

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, body.ToString());
        Assert.AreEqual("pending_square_order", body.Str("squareSyncStatus"));
        var orderId = body.Int("orderId");
        Assert.AreEqual(1, await app.WithDbAsync(db => db.Orders.CountAsync()), "the eShop order is kept while Square is unsettled");

        var settled = await MyOrderAsync(shopper, orderId);

        Assert.AreEqual("synced", settled.Str("squareSyncStatus"));
        Assert.AreEqual(1, app.Square.Orders.Count, "settling re-used the key; still one Square order");
        Assert.AreEqual((string?)app.Square.Orders.Single()["id"], settled.Str("squareOrderId"));
        Assert.AreEqual("Happy birthday, Sam!", settled.Str("giftMessage"));
        var keys = app.Square.RequestsTo("POST", "/v2/orders").Where(r => r.Path == "/v2/orders")
            .Select(r => (string?)r.Json!["idempotency_key"]).Distinct().ToList();
        Assert.AreEqual(1, keys.Count);
    }

    [TestMethod]
    public async Task GiftMessageUnknownOutcomeIsSettledWithSameKey()
    {
        await using var app = new SquareApiFactory();
        var ids = await app.CatalogItemIdsAsync();
        app.Square.Fail("POST", "/v2/orders/SQ-ORDER*", FakeSquare.FaultKind.DropAfterProcessing);

        var (response, body) = await PlaceAsync(app.ShopperClient(), OrderBody([ids[0]]));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, body.ToString());
        var upserts = app.Square.Requests.Where(r => r.Method == "POST" && r.Path.EndsWith($"/custom-attributes/{GiftKey}")).ToList();
        Assert.AreEqual(2, upserts.Count);
        Assert.AreEqual((string?)upserts[0].Json!["idempotency_key"], (string?)upserts[1].Json!["idempotency_key"]);
        Assert.AreEqual("Happy birthday, Sam!", app.Square.OrderAttributeValue(body.Str("squareOrderId")!, GiftKey));
    }

    [TestMethod]
    public async Task PendingGiftMessageIsSettledOnRead()
    {
        await using var app = new SquareApiFactory();
        var ids = await app.CatalogItemIdsAsync();
        var shopper = app.ShopperClient();
        app.Square.Fail("POST", "/v2/orders/SQ-ORDER*", FakeSquare.FaultKind.DropBeforeProcessing, times: 2);

        var (response, body) = await PlaceAsync(shopper, OrderBody([ids[0]]));

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, body.ToString());
        Assert.AreEqual("pending_gift_message", body.Str("squareSyncStatus"));
        Assert.IsNotNull(body.Str("squareOrderId"));

        var settled = await MyOrderAsync(shopper, body.Int("orderId"));
        Assert.AreEqual("synced", settled.Str("squareSyncStatus"));
        Assert.AreEqual("Happy birthday, Sam!", settled.Str("giftMessage"));
    }

    [TestMethod]
    public async Task GiftMessageThatLandedIsNotWrittenAgainWhenSettling()
    {
        await using var app = new SquareApiFactory();
        var ids = await app.CatalogItemIdsAsync();
        var shopper = app.ShopperClient();
        // Both sends reach Square (same key) but neither answer arrives.
        app.Square.Fail("POST", "/v2/orders/SQ-ORDER*", FakeSquare.FaultKind.DropAfterProcessing, times: 2);

        var (response, body) = await PlaceAsync(shopper, OrderBody([ids[0]]));
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, body.ToString());
        Assert.AreEqual("pending_gift_message", body.Str("squareSyncStatus"));

        var settled = await MyOrderAsync(shopper, body.Int("orderId"));

        Assert.AreEqual("synced", settled.Str("squareSyncStatus"));
        Assert.AreEqual("Happy birthday, Sam!", settled.Str("giftMessage"));
        var upserts = app.Square.Requests.Count(r => r.Method == "POST" && r.Path.EndsWith($"/custom-attributes/{GiftKey}"));
        Assert.AreEqual(2, upserts, "settling read the value Square already held instead of writing again");
    }

    [TestMethod]
    public async Task SettlingNeverOverwritesAStaffEdit()
    {
        await using var app = new SquareApiFactory();
        var ids = await app.CatalogItemIdsAsync();
        var shopper = app.ShopperClient();
        app.Square.Fail("POST", "/v2/orders/SQ-ORDER*", FakeSquare.FaultKind.DropBeforeProcessing, times: 2);
        var (_, body) = await PlaceAsync(shopper, OrderBody([ids[0]]));
        Assert.AreEqual("pending_gift_message", body.Str("squareSyncStatus"));

        app.Square.StaffEditsOrderAttribute(body.Str("squareOrderId")!, GiftKey, "Written by staff at the till");
        var settled = await MyOrderAsync(shopper, body.Int("orderId"));

        Assert.AreEqual("synced", settled.Str("squareSyncStatus"));
        Assert.AreEqual("Written by staff at the till", settled.Str("giftMessage"));
    }

    [TestMethod]
    public async Task DefinitionCreateUnknownOutcomeIsSettledByRead()
    {
        await using var app = new SquareApiFactory();
        var ids = await app.CatalogItemIdsAsync();
        app.Square.Fail("POST", "/v2/orders/custom-attribute-definitions", FakeSquare.FaultKind.DropAfterProcessing);

        var (response, body) = await PlaceAsync(app.ShopperClient(), OrderBody([ids[0]]));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, body.ToString());
        Assert.AreEqual(1, app.Square.RequestsTo("POST", "/v2/orders/custom-attribute-definitions").Count, "settled by reading, not by creating again");
        Assert.IsNotNull(app.Square.Definition(GiftKey));
    }

    [TestMethod]
    public async Task ExistingGiftFieldIsReusedAcrossOrders()
    {
        await using var app = new SquareApiFactory();
        var ids = await app.CatalogItemIdsAsync();
        var shopper = app.ShopperClient();

        await PlaceAsync(shopper, OrderBody([ids[0]]));
        await PlaceAsync(shopper, OrderBody([ids[1]], "Congratulations!"));

        Assert.AreEqual(1, app.Square.RequestsTo("POST", "/v2/orders/custom-attribute-definitions").Count);
        Assert.AreEqual(2, app.Square.Orders.Count);
    }

    [TestMethod]
    public async Task MyOrderStillReturnsTheEshopOrderWhenSquareIsUnreachable()
    {
        await using var app = new SquareApiFactory();
        var ids = await app.CatalogItemIdsAsync();
        var shopper = app.ShopperClient();
        var (_, placed) = await PlaceAsync(shopper, OrderBody([ids[0]]));
        app.Square.Fail("GET", "/v2/orders/*", FakeSquare.FaultKind.DropBeforeProcessing, times: 20);

        var order = await MyOrderAsync(shopper, placed.Int("orderId"));

        Assert.AreEqual(placed.Int("orderId"), order.Int("orderId"));
        Assert.IsNotNull(order.Str("squareError"), "the caller is told the Square part is missing");
        Assert.IsNull(order.Str("giftMessage"));
    }

    private static string ApiTokenHelperForOtherUser()
    {
        var claims = new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "someone-else@microsoft.com") };
        var key = System.Text.Encoding.ASCII.GetBytes(Microsoft.eShopWeb.ApplicationCore.Constants.AuthorizationConstants.JWT_SECRET_KEY);
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var token = handler.CreateToken(new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key), Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature),
        });
        return handler.WriteToken(token);
    }
}
