using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Square;

[TestClass]
public class SquareCatalogSyncTests
{
    private static Task<SquareCatalogSyncResult> Sync(SquareHarness h) =>
        h.Run<SquareCatalogSync, SquareCatalogSyncResult>(s => s.SyncAllAsync(default));

    private static JsonObject Variation(JsonObject item) => item["item_data"]!["variations"]![0]!.AsObject();

    [TestMethod]
    public async Task FirstSyncCreatesEveryItemWithTheSameNameAndPrice()
    {
        await using var h = new SquareHarness();
        await h.SeedCatalogAsync(("Mug", 8.5m), ("Shirt", 12m));

        var result = await Sync(h);

        Assert.AreEqual((2, 0, 0, 0), (result.Created, result.Updated, result.Unchanged, result.Failed));
        var items = h.Square.ItemsCreatedByShop().ToList();
        Assert.AreEqual(2, items.Count);
        var mug = items.Single(i => (string?)i["item_data"]!["name"] == "Mug");
        Assert.AreEqual(850L, Variation(mug)["item_variation_data"]!["price_money"]!["amount"]!.GetValue<long>());
        Assert.AreEqual("USD", (string?)Variation(mug)["item_variation_data"]!["price_money"]!["currency"]);
        Assert.AreEqual("FIXED_PRICING", (string?)Variation(mug)["item_variation_data"]!["pricing_type"]);
        StringAssert.StartsWith((string?)Variation(mug)["item_variation_data"]!["sku"], "eshop-item-");
    }

    [TestMethod]
    public async Task RunningAgainCreatesNoDuplicates()
    {
        await using var h = new SquareHarness();
        await h.SeedCatalogAsync(("Mug", 8.5m), ("Shirt", 12m));
        await Sync(h);

        var again = await Sync(h);

        Assert.AreEqual((0, 0, 2, 0), (again.Created, again.Updated, again.Unchanged, again.Failed));
        Assert.AreEqual(2, h.Square.ItemsCreatedByShop().Count());
        Assert.AreEqual(2, h.Square.RequestsTo(HttpMethod.Post, "/v2/catalog/object").Count(), "no writes when nothing changed");
    }

    [TestMethod]
    public async Task AChangedNameOrPriceIsUpdatedAndEverythingElseOnTheSquareItemIsKept()
    {
        await using var h = new SquareHarness();
        var seeded = await h.SeedCatalogAsync(("Mug", 8.5m), ("Shirt", 12m));
        await Sync(h);
        var mugId = h.Square.ItemsCreatedByShop().Single(i => (string?)i["item_data"]!["name"] == "Mug")["id"]!.GetValue<string>();
        h.Square.CatalogObjects[mugId]["item_data"]!["description"] = "Set by staff in Square";

        await h.Db(async db =>
        {
            var mug = await db.CatalogItems.SingleAsync(i => i.Id == seeded[0].Id);
            mug.UpdateDetails(new CatalogItem.CatalogItemDetails("Big Mug", mug.Description, 9.25m));
            return await db.SaveChangesAsync();
        });
        var result = await Sync(h);

        Assert.AreEqual((0, 1, 1, 0), (result.Created, result.Updated, result.Unchanged, result.Failed));
        var updated = h.Square.CatalogObjects[mugId];
        Assert.AreEqual("Big Mug", (string?)updated["item_data"]!["name"]);
        Assert.AreEqual(925L, Variation(updated)["item_variation_data"]!["price_money"]!["amount"]!.GetValue<long>());
        Assert.AreEqual("Set by staff in Square", (string?)updated["item_data"]!["description"], "upsert is full replacement: keep staff fields");
        var sent = h.Square.RequestsTo(HttpMethod.Post, "/v2/catalog/object").Last().Json!;
        Assert.AreEqual(1L, sent["object"]!["version"]!.GetValue<long>(), "the update carries the version it was based on");
        Assert.AreEqual(2, h.Square.ItemsCreatedByShop().Count());
    }

    [TestMethod]
    public async Task ItemsTheShopDidNotCreateAreLeftAlone()
    {
        await using var h = new SquareHarness();
        var foreign = h.Square.AddForeignItem("Mug"); // same name as an eShop item
        var before = h.Square.CatalogObjects[foreign].ToJsonString();
        await h.SeedCatalogAsync(("Mug", 8.5m));

        await Sync(h);
        await Sync(h);

        Assert.AreEqual(before, h.Square.CatalogObjects[foreign].ToJsonString());
        Assert.IsFalse(h.Square.Snapshot().Any(r => r.Body?.Contains(foreign) == true), "the foreign item is never sent");
    }

    [TestMethod]
    public async Task AnItemDeletedInSquareIsCreatedAgain()
    {
        await using var h = new SquareHarness();
        await h.SeedCatalogAsync(("Mug", 8.5m));
        await Sync(h);
        var first = h.Square.ItemsCreatedByShop().Single();
        first["is_deleted"] = true;

        var result = await Sync(h);

        Assert.AreEqual(1, result.Created);
        Assert.AreNotEqual(first["id"]!.GetValue<string>(), h.Square.ItemsCreatedByShop().Single()["id"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task LostLinksAreRecoveredFromSquareInsteadOfCreatingDuplicates()
    {
        await using var h = new SquareHarness();
        await h.SeedCatalogAsync(("Mug", 8.5m), ("Shirt", 12m));
        await Sync(h);
        await h.Db(async db =>
        {
            db.SquareCatalogLinks.RemoveRange(db.SquareCatalogLinks);
            return await db.SaveChangesAsync();
        });

        var result = await Sync(h);

        Assert.AreEqual((0, 0, 2, 0), (result.Created, result.Updated, result.Unchanged, result.Failed));
        Assert.AreEqual(2, h.Square.ItemsCreatedByShop().Count());
        Assert.AreEqual(2, await h.Db(db => db.SquareCatalogLinks.CountAsync(l => l.State == SquareCatalogLinkState.Synced)));
    }

    [TestMethod]
    public async Task CreateConnectionFailureThenRetrySettlesWithSameKey()
    {
        await using var h = new SquareHarness();
        await h.SeedCatalogAsync(("Mug", 8.5m));
        var lost = 0;
        h.Square.LoseResponse = r => r.Path == "/v2/catalog/object" && lost++ == 0; // Square creates it, the reply is lost

        var result = await Sync(h);

        var upserts = h.Square.RequestsTo(HttpMethod.Post, "/v2/catalog/object").ToList();
        Assert.AreEqual(2, upserts.Count);
        Assert.AreEqual((string?)upserts[0].Json!["idempotency_key"], (string?)upserts[1].Json!["idempotency_key"]);
        Assert.AreEqual(1, result.Created);
        Assert.AreEqual(1, h.Square.ItemsCreatedByShop().Count(), "the re-send returned the item Square already created");
    }

    [TestMethod]
    public async Task AnUnsettledCreateIsFinishedByTheNextSyncWithTheSameKey()
    {
        await using var h = new SquareHarness();
        await h.SeedCatalogAsync(("Mug", 8.5m));
        h.Square.LoseResponse = r => r.Path == "/v2/catalog/object"; // every reply lost

        var first = await Sync(h);
        Assert.AreEqual(1, first.Failed);
        var pending = await h.Db(db => db.SquareCatalogLinks.SingleAsync());
        Assert.AreEqual(SquareCatalogLinkState.PendingCreate, pending.State);

        h.Square.LoseResponse = null;
        var second = await Sync(h);

        Assert.AreEqual(1, second.Created);
        Assert.AreEqual(1, h.Square.ItemsCreatedByShop().Count());
        Assert.IsTrue(h.Square.RequestsTo(HttpMethod.Post, "/v2/catalog/object")
            .All(r => (string?)r.Json!["idempotency_key"] == pending.PendingIdempotencyKey));
    }

    [TestMethod]
    public async Task RateLimitedWritesAreSentAgain()
    {
        await using var h = new SquareHarness();
        await h.SeedCatalogAsync(("Mug", 8.5m));
        var limited = 0;
        h.Square.Intercept = r => r.Path == "/v2/catalog/object" && limited++ == 0
            ? FakeSquare.Error((HttpStatusCode)429, "RATE_LIMITED", "RATE_LIMIT_ERROR")
            : null;

        var result = await Sync(h);

        Assert.AreEqual(1, result.Created);
        Assert.AreEqual(1, h.Square.ItemsCreatedByShop().Count());
    }

    [TestMethod]
    public async Task ARejectedItemIsReportedAsFailedWithoutStoppingTheOthers()
    {
        await using var h = new SquareHarness();
        await h.SeedCatalogAsync(("Mug", 8.5m), ("Shirt", 12m));
        h.Square.Intercept = r => r.Path == "/v2/catalog/object" && r.Body!.Contains("Mug")
            ? FakeSquare.Error(HttpStatusCode.BadRequest, "INVALID_VALUE", "INVALID_REQUEST_ERROR")
            : null;

        var result = await Sync(h);

        Assert.AreEqual((1, 1), (result.Created, result.Failed));
        StringAssert.Contains(result.Items.Single(i => i.Name == "Mug").Error, "INVALID_VALUE");
    }

    [TestMethod]
    public async Task AfterARejectedCreateTheNextSyncUsesTheCurrentEShopData()
    {
        await using var h = new SquareHarness();
        var seeded = await h.SeedCatalogAsync(("Mug", 8.5m));
        h.Square.Intercept = r => r.Path == "/v2/catalog/object"
            ? FakeSquare.Error(HttpStatusCode.BadRequest, "INVALID_VALUE", "INVALID_REQUEST_ERROR")
            : null;
        Assert.AreEqual(1, (await Sync(h)).Failed);

        h.Square.Intercept = null;
        await h.Db(async db =>
        {
            var mug = await db.CatalogItems.SingleAsync(i => i.Id == seeded[0].Id);
            mug.UpdateDetails(new CatalogItem.CatalogItemDetails("Fixed Mug", mug.Description, 8.5m));
            return await db.SaveChangesAsync();
        });
        var result = await Sync(h);

        Assert.AreEqual(1, result.Created);
        Assert.AreEqual("Fixed Mug", (string?)h.Square.ItemsCreatedByShop().Single()["item_data"]!["name"]);
        var keys = h.Square.RequestsTo(HttpMethod.Post, "/v2/catalog/object").Select(r => (string?)r.Json!["idempotency_key"]).ToList();
        Assert.AreNotEqual(keys[0], keys[^1], "a refused request is not re-sent");
    }

    [TestMethod]
    public async Task TheStoreRefusesASecondClaimForTheSameItem()
    {
        await using var h = new SquareHarness();
        await h.Db(async db =>
        {
            db.SquareCatalogLinks.Add(new SquareCatalogLink { MerchantId = "M", CatalogItemId = 1 });
            return await db.SaveChangesAsync();
        });

        try
        {
            await h.Db(async db =>
            {
                db.SquareCatalogLinks.Add(new SquareCatalogLink { MerchantId = "M", CatalogItemId = 1 });
                return await db.SaveChangesAsync();
            });
            Assert.Fail("the second claim was accepted");
        }
        catch (Exception ex) when (SquareClaims.IsDuplicateKey(ex))
        {
            // refused, and recognised as a duplicate claim
        }
    }

    [TestMethod]
    public async Task ConcurrentSyncsCreateEachItemOnce()
    {
        await using var h = new SquareHarness();
        await h.SeedCatalogAsync(("Mug", 8.5m), ("Shirt", 12m), ("Cap", 5m));

        await Task.WhenAll(Sync(h), Sync(h), Sync(h));

        Assert.AreEqual(3, h.Square.ItemsCreatedByShop().Count());
    }
}
