using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Square;

[TestClass]
public class SquareOrderSyncTests
{
    private const string Buyer = "demouser@microsoft.com";

    private static Task<PlacedOrder> Place(SquareHarness h, PlaceOrderCommand command, string buyer = Buyer) =>
        h.Run<SquareOrderService, PlacedOrder>(s => s.PlaceOrderAsync(buyer, command, default));

    private static Task<MyOrder?> Get(SquareHarness h, int orderId, string buyer = Buyer) =>
        h.Run<SquareOrderService, MyOrder?>(s => s.GetMyOrderAsync(buyer, orderId, default));

    private static PlaceOrderCommand Command(int itemId, string? gift = null, int quantity = 2) =>
        new([new PlaceOrderLine(itemId, quantity)], gift, null);

    [TestMethod]
    public async Task AnOrderIsCreatedInSquareAtTheMerchantLocationWithTheSameItemsAndPrices()
    {
        await using var h = new SquareHarness();
        var items = await h.SeedCatalogAsync(("Mug", 8.5m), ("Shirt", 12m));

        var placed = await Place(h, new PlaceOrderCommand(
            [new PlaceOrderLine(items[0].Id, 2), new PlaceOrderLine(items[1].Id, 1), new PlaceOrderLine(items[0].Id, 1)], null, null));

        Assert.AreEqual(SquareOrderStatus.Created, placed.SquareStatus);
        var squareOrder = h.Square.Orders[placed.SquareOrderId!];
        Assert.AreEqual(h.Square.LocationId, (string?)squareOrder["location_id"]);
        Assert.AreEqual(placed.OrderId.ToString(), (string?)squareOrder["reference_id"]);
        var lines = squareOrder["line_items"]!.AsArray();
        var mug = lines.Single(l => (string?)l!["name"] == "Mug")!;
        Assert.AreEqual("3", (string?)mug["quantity"]);
        Assert.AreEqual(850L, mug["base_price_money"]!["amount"]!.GetValue<long>());
        Assert.AreEqual("USD", (string?)mug["base_price_money"]!["currency"]);
        Assert.AreEqual("1", (string?)lines.Single(l => (string?)l!["name"] == "Shirt")!["quantity"]);

        // The eShop order uses the existing Order/OrderItem model.
        var order = await h.Db(db => db.Orders.Include(o => o.OrderItems).SingleAsync(o => o.Id == placed.OrderId));
        Assert.AreEqual(Buyer, order.BuyerId);
        Assert.AreEqual(37.5m, order.Total());
        Assert.AreEqual("1 Main St", order.ShipToAddress.Street, "no address given: pickup at the Square location");
    }

    [TestMethod]
    public async Task TheGiftMessageLivesOnTheSquareOrderAndNotInTheShopDatabase()
    {
        await using var h = new SquareHarness();
        var item = (await h.SeedCatalogAsync(("Mug", 8.5m))).Single();
        const string message = "Happy birthday, Sam!";

        var placed = await Place(h, Command(item.Id, message));

        Assert.IsTrue(placed.GiftMessageSaved);
        Assert.AreEqual(message, h.Square.GiftMessages[placed.SquareOrderId!].Value);
        var definition = h.Square.GiftDefinition!;
        Assert.AreEqual("gift-message", (string?)definition["key"]);
        Assert.AreEqual("Gift message", (string?)definition["name"]);
        Assert.AreEqual("VISIBILITY_READ_WRITE_VALUES", (string?)definition["visibility"]);
        Assert.AreEqual("https://developer-production-s.squarecdn.com/schemas/v1/common.json#squareup.common.String",
            (string?)definition["schema"]!["$ref"]);

        var link = await h.Db(db => db.SquareOrderLinks.SingleAsync());
        var stored = string.Join("|", link.MerchantId, link.LocationId, link.IdempotencyKey, link.SquareOrderId, link.LastErrorCode, link.State);
        Assert.IsFalse(stored.Contains(message), "the shop's database never holds the gift message");
    }

    [TestMethod]
    public async Task TheGiftMessageFieldIsDefinedOncePerMerchant()
    {
        await using var h = new SquareHarness();
        var item = (await h.SeedCatalogAsync(("Mug", 8.5m))).Single();
        await Place(h, Command(item.Id, "one"));
        await Place(h, Command(item.Id, "two"));
        Assert.AreEqual(1, h.Square.RequestsTo(HttpMethod.Post, "/v2/orders/custom-attribute-definitions").Count());
    }

    [TestMethod]
    public async Task ReadingTheOrderShowsTheGiftMessageAsSquareHoldsItNow()
    {
        await using var h = new SquareHarness();
        var item = (await h.SeedCatalogAsync(("Mug", 8.5m))).Single();
        var placed = await Place(h, Command(item.Id, "Original message"));

        h.Square.StaffEditsGiftMessage(placed.SquareOrderId!, "Edited by staff");
        var mine = await Get(h, placed.OrderId);

        Assert.AreEqual("Edited by staff", mine!.GiftMessage);
        Assert.IsTrue(mine.GiftMessageAvailable);
        Assert.AreEqual(placed.SquareOrderId, mine.SquareOrderId);
        Assert.AreEqual("OPEN", mine.SquareOrderState);
    }

    [TestMethod]
    public async Task AnOrderWithoutAGiftMessageReadsBackNull()
    {
        await using var h = new SquareHarness();
        var item = (await h.SeedCatalogAsync(("Mug", 8.5m))).Single();
        var placed = await Place(h, Command(item.Id));

        var mine = await Get(h, placed.OrderId);

        Assert.IsNull(mine!.GiftMessage);
        Assert.IsTrue(mine.GiftMessageAvailable);
        Assert.AreEqual(0, h.Square.RequestsTo(HttpMethod.Post, "/v2/orders/custom-attribute-definitions").Count());
    }

    [TestMethod]
    public async Task AnotherShopperCannotSeeTheOrder()
    {
        await using var h = new SquareHarness();
        var item = (await h.SeedCatalogAsync(("Mug", 8.5m))).Single();
        var placed = await Place(h, Command(item.Id, "secret"));
        var before = h.Square.Snapshot().Count;

        Assert.IsNull(await Get(h, placed.OrderId, buyer: "someone.else@microsoft.com"));
        Assert.AreEqual(before, h.Square.Snapshot().Count, "nothing about the order is read from Square for another shopper");
    }

    [TestMethod]
    public async Task UnknownCatalogItemsAreRejectedBeforeAnythingIsStored()
    {
        await using var h = new SquareHarness();
        await Assert.ThrowsExceptionAsync<OrderRequestException>(() => Place(h, Command(12345)));
        Assert.AreEqual(0, await h.Db(db => db.Orders.CountAsync()));
        Assert.AreEqual(0, h.Square.Snapshot().Count);
    }

    [TestMethod]
    public async Task CreateOrderConnectionFailureIsSettledWithSameKey()
    {
        await using var h = new SquareHarness();
        var item = (await h.SeedCatalogAsync(("Mug", 8.5m))).Single();
        var lost = 0;
        h.Square.LoseResponse = r => r.Path == "/v2/orders" && lost++ == 0;

        var placed = await Place(h, Command(item.Id, "hi"));

        var creates = h.Square.RequestsTo(HttpMethod.Post, "/v2/orders").Where(r => r.Path == "/v2/orders").ToList();
        Assert.AreEqual(2, creates.Count);
        Assert.AreEqual((string?)creates[0].Json!["idempotency_key"], (string?)creates[1].Json!["idempotency_key"]);
        Assert.AreEqual(1, h.Square.Orders.Count, "one Square order, not two");
        Assert.AreEqual(SquareOrderStatus.Created, placed.SquareStatus);
        Assert.AreEqual("hi", h.Square.GiftMessages[placed.SquareOrderId!].Value);
    }

    [TestMethod]
    public async Task AnUnconfirmedOrderStaysPendingAndIsCompletedWhenRead()
    {
        await using var h = new SquareHarness();
        var item = (await h.SeedCatalogAsync(("Mug", 8.5m))).Single();
        h.Square.Intercept = r => r.Path == "/v2/orders" ? throw new HttpRequestException("connection reset") : null;

        var placed = await Place(h, Command(item.Id));
        Assert.AreEqual(SquareOrderStatus.Pending, placed.SquareStatus);
        Assert.AreEqual(SquareOrderLinkState.Pending, (await h.Db(db => db.SquareOrderLinks.SingleAsync())).State);

        h.Square.Intercept = null;
        var mine = await Get(h, placed.OrderId);

        Assert.AreEqual(SquareOrderStatus.Created, mine!.SquareStatus);
        Assert.AreEqual(1, h.Square.Orders.Count);
        var keys = h.Square.RequestsTo(HttpMethod.Post, "/v2/orders").Where(r => r.Path == "/v2/orders")
            .Select(r => (string?)r.Json!["idempotency_key"]).Distinct().ToList();
        Assert.AreEqual(1, keys.Count, "every attempt used the same idempotency key");
    }

    [TestMethod]
    public async Task AnOrderSquareRejectsIsWithdrawn()
    {
        await using var h = new SquareHarness();
        var item = (await h.SeedCatalogAsync(("Mug", 8.5m))).Single();
        h.Square.Intercept = r => r.Path == "/v2/orders"
            ? FakeSquare.Error(HttpStatusCode.BadRequest, "INVALID_VALUE", "INVALID_REQUEST_ERROR")
            : null;

        var ex = await Assert.ThrowsExceptionAsync<SquareIntegrationException>(() => Place(h, Command(item.Id)));

        Assert.AreEqual(SquareFailureKind.Rejected, ex.Kind);
        Assert.AreEqual(0, await h.Db(db => db.Orders.CountAsync()));
        Assert.AreEqual(0, await h.Db(db => db.SquareOrderLinks.CountAsync()));
    }

    [TestMethod]
    public async Task GiftMessageConnectionFailureIsResent()
    {
        await using var h = new SquareHarness();
        var item = (await h.SeedCatalogAsync(("Mug", 8.5m))).Single();
        var lost = 0;
        h.Square.LoseResponse = r => r.Path.EndsWith("/custom-attributes/gift-message") && r.Method == HttpMethod.Post && lost++ == 0;

        var placed = await Place(h, Command(item.Id, "Congrats!"));

        Assert.IsTrue(placed.GiftMessageSaved);
        Assert.AreEqual(("Congrats!", 1), h.Square.GiftMessages[placed.SquareOrderId!], "settled by reading it back; written once");
    }

    [TestMethod]
    public async Task GiftMessageNeverWrittenIsWrittenOnSettlement()
    {
        await using var h = new SquareHarness();
        var item = (await h.SeedCatalogAsync(("Mug", 8.5m))).Single();
        var failed = 0;
        h.Square.Intercept = r => r.Path.EndsWith("/custom-attributes/gift-message") && r.Method == HttpMethod.Post && failed++ == 0
            ? throw new HttpRequestException("connection refused")
            : null;

        var placed = await Place(h, Command(item.Id, "Congrats!"));

        Assert.IsTrue(placed.GiftMessageSaved);
        Assert.AreEqual("Congrats!", h.Square.GiftMessages[placed.SquareOrderId!].Value);
    }

    [TestMethod]
    public async Task DefinitionCreateConnectionFailureIsSettledByRetrieve()
    {
        await using var h = new SquareHarness();
        var item = (await h.SeedCatalogAsync(("Mug", 8.5m))).Single();
        h.Square.LoseResponse = r => r.Path == "/v2/orders/custom-attribute-definitions";

        var placed = await Place(h, Command(item.Id, "hello"));

        Assert.IsTrue(placed.GiftMessageSaved);
        Assert.AreEqual(1, h.Square.RequestsTo(HttpMethod.Post, "/v2/orders/custom-attribute-definitions").Count());
        Assert.AreEqual(2, h.Square.RequestsTo(HttpMethod.Get, "/v2/orders/custom-attribute-definitions/gift-message").Count());
    }

    [TestMethod]
    public async Task WhenSquareCannotBeReadTheGiftMessageIsReportedUnavailableNotEmpty()
    {
        await using var h = new SquareHarness();
        var item = (await h.SeedCatalogAsync(("Mug", 8.5m))).Single();
        var placed = await Place(h, Command(item.Id, "hello"));
        h.Square.Intercept = r => r.Method == HttpMethod.Get && r.Path.StartsWith("/v2/orders/")
            ? FakeSquare.Error(HttpStatusCode.ServiceUnavailable, "SERVICE_UNAVAILABLE", "API_ERROR")
            : null;

        var mine = await Get(h, placed.OrderId);

        Assert.IsFalse(mine!.GiftMessageAvailable);
        Assert.IsNull(mine.GiftMessage);
        Assert.IsNotNull(mine.Warning);
    }
}
