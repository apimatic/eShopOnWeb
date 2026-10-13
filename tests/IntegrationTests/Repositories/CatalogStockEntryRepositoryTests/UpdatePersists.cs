using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Data;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Repositories.CatalogStockEntryRepositoryTests;

public class UpdatePersists
{
    private static readonly InMemoryDatabaseRoot SharedRoot = new();

    private CatalogContext NewContext(string db) =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(db).Options);

    private CatalogContext SharedContext(string db) =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(db, SharedRoot).Options);

    [Fact]
    public async Task UpdateAsync_persists_changed_quantity()
    {
        var db = "StockUpdate_" + System.Guid.NewGuid();

        var ctx = NewContext(db);
        var repo = new EfRepository<CatalogStockEntry>(ctx);
        await repo.AddAsync(new CatalogStockEntry(2, 50));

        var entry = await repo.FirstOrDefaultAsync(new CatalogStockEntryByCatalogItemIdSpecification(2));
        Assert.NotNull(entry);
        entry!.SetQuantity(33);
        await repo.UpdateAsync(entry);

        var reread = await repo.FirstOrDefaultAsync(new CatalogStockEntryByCatalogItemIdSpecification(2));
        Assert.Equal(33, reread!.Quantity);
    }

    [Fact]
    public async Task UpdateAsync_persists_across_separate_contexts_sharing_store()
    {
        var db = "StockUpdateShared_" + System.Guid.NewGuid();

        // request A: add
        using (var c = SharedContext(db))
        {
            var repo = new EfRepository<CatalogStockEntry>(c);
            await repo.AddAsync(new CatalogStockEntry(2, 50));
        }

        // request C: load + update (separate context, shared store)
        using (var c = SharedContext(db))
        {
            var repo = new EfRepository<CatalogStockEntry>(c);
            var entry = await repo.FirstOrDefaultAsync(new CatalogStockEntryByCatalogItemIdSpecification(2));
            entry!.SetQuantity(33);
            await repo.UpdateAsync(entry);
        }

        // request D: reread (separate context, shared store)
        using (var c = SharedContext(db))
        {
            var repo = new EfRepository<CatalogStockEntry>(c);
            var reread = await repo.FirstOrDefaultAsync(new CatalogStockEntryByCatalogItemIdSpecification(2));
            Assert.Equal(33, reread!.Quantity);
        }
    }

    [Fact]
    public async Task CatalogStockService_SetStock_add_then_update_across_requests_persists()
    {
        var db = "StockSvc_" + System.Guid.NewGuid();
        var order = Substitute.For<IRepository<Order>>();
        var uri = Substitute.For<IUriComposer>();

        // seed a catalog item and capture its generated id
        int itemId;
        using (var c = SharedContext(db))
        {
            var item = new CatalogItem(1, 1, "desc", "name", 1m, "pic.png");
            c.Add(item);
            await c.SaveChangesAsync();
            itemId = item.Id;
        }

        CatalogStockService Svc(CatalogContext c) =>
            new(new EfRepository<CatalogStockEntry>(c), new EfRepository<CatalogItem>(c), order, uri);

        using (var c = SharedContext(db))
        {
            await Svc(c).SetStockAsync(itemId, 50, default);
        }
        using (var c = SharedContext(db))
        {
            await Svc(c).SetStockAsync(itemId, 33, default);
        }
        using (var c = SharedContext(db))
        {
            var level = await Svc(c).GetStockAsync(itemId, default);
            Assert.Equal(33, level);
        }
    }
}
