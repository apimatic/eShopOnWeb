using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SquareIntegration;

[TestClass]
public class SquareCatalogSyncEndpointTests
{
    [TestMethod]
    public async Task FirstSyncCreatesEveryCatalogItemWithSameNameAndPrice()
    {
        await using var app = new SquareApiFactory();
        var items = await app.WithDbAsync(db => db.CatalogItems.AsNoTracking().ToListAsync());

        var result = await app.SyncAsync();

        Assert.AreEqual(items.Count, result.Int("created"));
        Assert.AreEqual(0, result.Int("updated"));
        Assert.AreEqual(0, result.Int("unchanged"));
        Assert.IsTrue(result.GetProperty("complete").GetBoolean());
        foreach (var item in items)
        {
            var squareItem = app.Square.ItemWithSku(SquareConstants.SkuFor(item.Id));
            Assert.IsNotNull(squareItem, $"item {item.Id} exists in Square");
            Assert.AreEqual(item.Name, FakeSquare.ItemName(squareItem));
            Assert.AreEqual((long)(item.Price * 100), FakeSquare.ItemPrice(squareItem));
        }

        var links = await app.WithDbAsync(db => db.SquareCatalogLinks.CountAsync());
        Assert.AreEqual(items.Count, links);
    }

    [TestMethod]
    public async Task RunningSyncAgainCreatesNoDuplicates()
    {
        await using var app = new SquareApiFactory();
        var first = await app.SyncAsync();
        var itemCount = app.Square.Items.Count;

        var second = await app.SyncAsync();

        Assert.AreEqual(0, second.Int("created"));
        Assert.AreEqual(0, second.Int("updated"));
        Assert.AreEqual(first.Int("created"), second.Int("unchanged"));
        Assert.AreEqual(itemCount, app.Square.Items.Count);
    }

    [TestMethod]
    public async Task ChangedNameOrPriceIsUpdatedInSquare()
    {
        await using var app = new SquareApiFactory();
        await app.SyncAsync();
        var ids = await app.CatalogItemIdsAsync();
        await app.WithDbAsync(async db =>
        {
            var renamed = await db.CatalogItems.FindAsync(ids[0]);
            renamed!.UpdateDetails(new CatalogItem.CatalogItemDetails("Renamed Mug", renamed.Description, renamed.Price));
            var repriced = await db.CatalogItems.FindAsync(ids[1]);
            repriced!.UpdateDetails(new CatalogItem.CatalogItemDetails(repriced.Name, repriced.Description, 42.42m));
            await db.SaveChangesAsync();
        });

        var result = await app.SyncAsync();

        Assert.AreEqual(0, result.Int("created"));
        Assert.AreEqual(2, result.Int("updated"));
        Assert.AreEqual(ids.Length - 2, result.Int("unchanged"));
        Assert.AreEqual("Renamed Mug", FakeSquare.ItemName(app.Square.ItemWithSku(SquareConstants.SkuFor(ids[0]))!));
        Assert.AreEqual(4242L, FakeSquare.ItemPrice(app.Square.ItemWithSku(SquareConstants.SkuFor(ids[1]))!));

        var upsert = app.Square.RequestsTo("POST", "/v2/catalog/batch-upsert").Last();
        var sentItem = upsert.Json!["batches"]![0]!["objects"]![0]!;
        Assert.IsNotNull(sentItem["version"], "updates carry the version read from Square");
        Assert.IsFalse(((string)sentItem["id"]!).StartsWith('#'));
    }

    [TestMethod]
    public async Task ItemsTheShopDidNotCreateAreLeftAlone()
    {
        await using var app = new SquareApiFactory();
        app.Square.AddForeignItem("FOREIGN-1", "Counter Snack", 199);

        await app.SyncAsync();
        await app.SyncAsync();

        var foreign = app.Square.Items.Single(i => (string?)i["id"] == "FOREIGN-1");
        Assert.AreEqual("Counter Snack", FakeSquare.ItemName(foreign));
        Assert.AreEqual(199L, FakeSquare.ItemPrice(foreign));
        Assert.AreEqual(1L, (long)foreign["version"]!);
        Assert.IsFalse(app.Square.RequestsTo("POST", "/v2/catalog/batch-upsert")
            .Any(r => r.Body!.Contains("FOREIGN-1")), "foreign items are never written");
    }

    [TestMethod]
    public async Task ItemsAreRecognisedBySkuWhenLocalLinksAreLost()
    {
        await using var app = new SquareApiFactory();
        var first = await app.SyncAsync();
        await app.WithDbAsync(async db =>
        {
            db.SquareCatalogLinks.RemoveRange(db.SquareCatalogLinks);
            await db.SaveChangesAsync();
        });

        var second = await app.SyncAsync();

        Assert.AreEqual(0, second.Int("created"));
        Assert.AreEqual(first.Int("created"), second.Int("unchanged"));
        Assert.AreEqual(first.Int("created"), app.Square.Items.Count);
        Assert.IsTrue(app.Square.RequestsTo("POST", "/v2/catalog/search").Count > 0);
    }

    [TestMethod]
    public async Task UnknownOutcomeIsSettledByResendWithSameKey()
    {
        await using var app = new SquareApiFactory();
        var expected = (await app.CatalogItemIdsAsync()).Length;
        app.Square.Fail("POST", "/v2/catalog/batch-upsert", FakeSquare.FaultKind.DropAfterProcessing);

        var result = await app.SyncAsync();

        Assert.AreEqual(expected, result.Int("created"));
        Assert.AreEqual(expected, app.Square.Items.Count, "the re-send did not create duplicates");
        var sends = app.Square.RequestsTo("POST", "/v2/catalog/batch-upsert");
        Assert.AreEqual(2, sends.Count);
        Assert.AreEqual((string?)sends[0].Json!["idempotency_key"], (string?)sends[1].Json!["idempotency_key"]);
    }

    [TestMethod]
    public async Task UnsettledOutcomeIsReportedAndTheNextSyncDoesNotDuplicate()
    {
        await using var app = new SquareApiFactory();
        var expected = (await app.CatalogItemIdsAsync()).Length;
        app.Square.Fail("POST", "/v2/catalog/batch-upsert", FakeSquare.FaultKind.DropAfterProcessing, times: 2);

        var response = await app.AdminClient().PostAsync("api/square/catalog/sync", null);
        Assert.AreEqual(HttpStatusCode.GatewayTimeout, response.StatusCode);

        var retry = await app.SyncAsync();
        Assert.AreEqual(0, retry.Int("created"));
        Assert.AreEqual(expected, retry.Int("unchanged"));
        Assert.AreEqual(expected, app.Square.Items.Count);
    }

    [TestMethod]
    public async Task ConcurrentSyncIsRefused()
    {
        await using var app = new SquareApiFactory();
        await app.WithDbAsync(async db =>
        {
            db.SquareLeases.Add(new SquareLease { Name = "catalog-sync", Owner = "other", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1) });
            await db.SaveChangesAsync();
        });

        var response = await app.AdminClient().PostAsync("api/square/catalog/sync", null);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.AreEqual(0, app.Square.RequestsTo("POST", "/v2/catalog/batch-upsert").Count);
    }

    [TestMethod]
    public async Task ExpiredClaimIsTakenOver()
    {
        await using var app = new SquareApiFactory();
        await app.WithDbAsync(async db =>
        {
            db.SquareLeases.Add(new SquareLease { Name = "catalog-sync", Owner = "crashed", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
            await db.SaveChangesAsync();
        });

        var result = await app.SyncAsync();

        Assert.IsTrue(result.Int("created") > 0);
        Assert.AreEqual(0, await app.WithDbAsync(db => db.SquareLeases.CountAsync()), "the claim is released after the sync");
    }

    [TestMethod]
    public async Task SquareRejectingTheBatchIsReportedAsBadGateway()
    {
        await using var app = new SquareApiFactory();
        app.Square.Fail("POST", "/v2/catalog/batch-upsert", FakeSquare.FaultKind.Respond, status: HttpStatusCode.BadRequest, code: "INVALID_VALUE");

        var response = await app.AdminClient().PostAsync("api/square/catalog/sync", null);

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(SquareApiFactory.Json);
        StringAssert.Contains(body.Str("message"), "INVALID_VALUE");
        Assert.AreEqual(1, app.Square.RequestsTo("POST", "/v2/catalog/batch-upsert").Count, "a definitive rejection is not re-sent");
    }
}
