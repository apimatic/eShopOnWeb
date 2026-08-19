using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class SupplierCatalogSyncServiceTests
{
    // A reader stub that returns a canned set of products, standing in for Firecrawl.
    private sealed class StubReader : ISupplierProductReader
    {
        private readonly SupplierProductReadResult _result;
        public int Calls { get; private set; }

        public StubReader(SupplierProductReadResult result) => _result = result;

        public Task<SupplierProductReadResult> ReadListingAsync(string listingUrl, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }

    private static CatalogContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase($"sync-tests-{Guid.NewGuid()}")
            .Options);

    private static SupplierCatalogSyncService BuildService(CatalogContext context, ISupplierProductReader reader) =>
        new(
            new EfRepository<CatalogSync>(context),
            new EfRepository<Supplier>(context),
            new EfRepository<SupplierProductLink>(context),
            new EfRepository<CatalogItem>(context),
            new EfRepository<CatalogBrand>(context),
            new EfRepository<CatalogType>(context),
            reader,
            Substitute.For<IAppLogger<SupplierCatalogSyncService>>());

    private static SupplierProduct Product(string name, string brand, string price, string sku) =>
        new() { Name = name, Description = $"{name} description", Brand = brand, Price = price, Sku = sku };

    private static async Task<(Supplier supplier, CatalogSync sync)> SeedSupplierAndSyncAsync(CatalogContext context)
    {
        var supplier = new Supplier("Test Supplier", "https://example.test/catalog");
        context.Suppliers.Add(supplier);
        var sync = new CatalogSync(supplier.Id);
        context.CatalogSyncs.Add(sync);
        await context.SaveChangesAsync();
        return (supplier, sync);
    }

    [Fact]
    public async Task ImportsProductsAndReportsPartialWhenOneHasNoPrice()
    {
        using var context = NewContext();
        var (_, sync) = await SeedSupplierAndSyncAsync(context);

        var reader = new StubReader(SupplierProductReadResult.Success(new List<SupplierProduct>
        {
            Product("Ergo Chair", "Kestrel", "$189.99", "KES-1042"),
            Product("Oak Desk", "Alderwood", "$349.00", "ALD-2210"),
            Product("Ink Cartridge", "Thistlewood", "Contact for pricing", "TSW-0090"),
        }));

        var service = BuildService(context, reader);
        await service.ExecuteAsync(sync.Id);

        var updated = await context.CatalogSyncs.FindAsync(sync.Id);
        Assert.Equal(SyncStatus.PartiallyImported, updated!.Status);
        Assert.Equal(3, updated.ItemsFound);
        Assert.Equal(2, updated.ItemsImported);

        Assert.Equal(2, await context.CatalogItems.CountAsync());
        Assert.Equal(2, await context.SupplierProductLinks.CountAsync());
    }

    [Fact]
    public async Task AllValidProductsReportsCompleted()
    {
        using var context = NewContext();
        var (_, sync) = await SeedSupplierAndSyncAsync(context);

        var reader = new StubReader(SupplierProductReadResult.Success(new List<SupplierProduct>
        {
            Product("Ergo Chair", "Kestrel", "$189.99", "KES-1042"),
            Product("Oak Desk", "Alderwood", "$349.00", "ALD-2210"),
        }));

        var service = BuildService(context, reader);
        await service.ExecuteAsync(sync.Id);

        var updated = await context.CatalogSyncs.FindAsync(sync.Id);
        Assert.Equal(SyncStatus.Completed, updated!.Status);
        Assert.Equal(2, updated.ItemsFound);
        Assert.Equal(2, updated.ItemsImported);
    }

    [Fact]
    public async Task ReRunningSyncDoesNotDuplicateProducts()
    {
        using var context = NewContext();
        var supplier = new Supplier("Test Supplier", "https://example.test/catalog");
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var reader = new StubReader(SupplierProductReadResult.Success(new List<SupplierProduct>
        {
            Product("Ergo Chair", "Kestrel", "$189.99", "KES-1042"),
            Product("Oak Desk", "Alderwood", "$349.00", "ALD-2210"),
        }));

        // First sync
        var firstSync = new CatalogSync(supplier.Id);
        context.CatalogSyncs.Add(firstSync);
        await context.SaveChangesAsync();
        await BuildService(context, reader).ExecuteAsync(firstSync.Id);

        Assert.Equal(2, await context.CatalogItems.CountAsync());

        // Second sync against the same listing must update, not duplicate.
        var secondSync = new CatalogSync(supplier.Id);
        context.CatalogSyncs.Add(secondSync);
        await context.SaveChangesAsync();
        await BuildService(context, reader).ExecuteAsync(secondSync.Id);

        Assert.Equal(2, await context.CatalogItems.CountAsync());
        Assert.Equal(2, await context.SupplierProductLinks.CountAsync());

        var updated = await context.CatalogSyncs.FindAsync(secondSync.Id);
        Assert.Equal(SyncStatus.Completed, updated!.Status);
        Assert.Equal(2, updated.ItemsImported);
    }

    [Fact]
    public async Task UpdatedPriceOnReRunUpdatesExistingCatalogItem()
    {
        using var context = NewContext();
        var supplier = new Supplier("Test Supplier", "https://example.test/catalog");
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var firstSync = new CatalogSync(supplier.Id);
        context.CatalogSyncs.Add(firstSync);
        await context.SaveChangesAsync();
        await BuildService(context, new StubReader(SupplierProductReadResult.Success(new List<SupplierProduct>
        {
            Product("Ergo Chair", "Kestrel", "$189.99", "KES-1042"),
        }))).ExecuteAsync(firstSync.Id);

        var secondSync = new CatalogSync(supplier.Id);
        context.CatalogSyncs.Add(secondSync);
        await context.SaveChangesAsync();
        await BuildService(context, new StubReader(SupplierProductReadResult.Success(new List<SupplierProduct>
        {
            Product("Ergo Chair", "Kestrel", "$199.99", "KES-1042"),
        }))).ExecuteAsync(secondSync.Id);

        Assert.Equal(1, await context.CatalogItems.CountAsync());
        var item = await context.CatalogItems.SingleAsync();
        Assert.Equal(199.99m, item.Price);
    }

    [Fact]
    public async Task FailedReadMarksSyncFailed()
    {
        using var context = NewContext();
        var (_, sync) = await SeedSupplierAndSyncAsync(context);

        var reader = new StubReader(SupplierProductReadResult.Failure("listing unreachable"));
        await BuildService(context, reader).ExecuteAsync(sync.Id);

        var updated = await context.CatalogSyncs.FindAsync(sync.Id);
        Assert.Equal(SyncStatus.Failed, updated!.Status);
        Assert.Equal(0, await context.CatalogItems.CountAsync());
        Assert.Contains("listing unreachable", updated.Detail);
    }
}
