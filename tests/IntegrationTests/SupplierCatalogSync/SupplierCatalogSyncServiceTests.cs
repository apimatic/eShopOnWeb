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

namespace Microsoft.eShopWeb.IntegrationTests.SupplierCatalogSync;

/// <summary>
/// Exercises <see cref="SupplierCatalogSyncService"/> against real EF in-memory repositories with a
/// substituted <see cref="ISupplierCatalogScraper"/>, so the matching/idempotency/status logic is
/// verified without touching the network.
/// </summary>
public class SupplierCatalogSyncServiceTests
{
    private readonly CatalogContext _context;
    private readonly ISupplierCatalogScraper _scraper = Substitute.For<ISupplierCatalogScraper>();

    public SupplierCatalogSyncServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName: $"SupplierSync-{Guid.NewGuid()}")
            .Options;
        _context = new CatalogContext(options);
    }

    private SupplierCatalogSyncService CreateService() => new(
        new EfRepository<CatalogSync>(_context),
        new EfRepository<Supplier>(_context),
        new EfRepository<CatalogItem>(_context),
        new EfRepository<CatalogBrand>(_context),
        new EfRepository<CatalogType>(_context),
        new EfRepository<SupplierProductMap>(_context),
        _scraper,
        Substitute.For<IAppLogger<SupplierCatalogSyncService>>());

    private async Task<int> SeedSupplierAndSyncAsync()
    {
        var supplier = new Supplier("Acme", "https://acme.test/catalog");
        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync();

        var sync = new CatalogSync(supplier.Id);
        _context.CatalogSyncs.Add(sync);
        await _context.SaveChangesAsync();
        return sync.Id;
    }

    private void ScraperReturns(bool fullyCaptured, params ScrapedProduct[] products) =>
        _scraper.ScrapeListingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SupplierScrapeResult(products, fullyCaptured));

    private static ScrapedProduct Product(string sku, string name, decimal? price, string brand = "Widgets") =>
        new() { ExternalId = sku, Name = name, Description = $"{name} description", Price = price, Brand = brand };

    [Fact]
    public async Task ImportsProductsIntoCatalogAndReportsCounts()
    {
        int syncId = await SeedSupplierAndSyncAsync();
        ScraperReturns(true,
            Product("A-1", "Alpha", 10.00m),
            Product("A-2", "Beta", 20.00m, brand: "Gizmos"));

        await CreateService().RunSyncAsync(syncId);

        var sync = await _context.CatalogSyncs.FindAsync(syncId);
        Assert.Equal(CatalogSyncStatus.Completed, sync!.Status);
        Assert.Equal(2, sync.ItemsFound);
        Assert.Equal(2, sync.ItemsImported);

        Assert.Equal(2, await _context.CatalogItems.CountAsync());
        Assert.Contains(_context.CatalogItems, i => i.Name == "Alpha" && i.Price == 10.00m);
        // Brands are created on demand from the supplier's brand names.
        Assert.Contains(_context.CatalogBrands, b => b.Brand == "Widgets");
        Assert.Contains(_context.CatalogBrands, b => b.Brand == "Gizmos");
    }

    [Fact]
    public async Task MarksPartiallyCompletedWhenAProductCannotBeImported()
    {
        int syncId = await SeedSupplierAndSyncAsync();
        // The second product has no usable price (e.g. "Contact for pricing") -> found but not imported.
        ScraperReturns(true,
            Product("A-1", "Alpha", 10.00m),
            Product("A-2", "NoPrice", null));

        await CreateService().RunSyncAsync(syncId);

        var sync = await _context.CatalogSyncs.FindAsync(syncId);
        Assert.Equal(CatalogSyncStatus.PartiallyCompleted, sync!.Status);
        Assert.Equal(2, sync.ItemsFound);
        Assert.Equal(1, sync.ItemsImported);
        Assert.Equal(1, await _context.CatalogItems.CountAsync());
    }

    [Fact]
    public async Task MarksPartiallyCompletedWhenListingNotFullyCaptured()
    {
        int syncId = await SeedSupplierAndSyncAsync();
        // Every product imports, but the crawl could not read the whole listing.
        ScraperReturns(false, Product("A-1", "Alpha", 10.00m));

        await CreateService().RunSyncAsync(syncId);

        var sync = await _context.CatalogSyncs.FindAsync(syncId);
        Assert.Equal(CatalogSyncStatus.PartiallyCompleted, sync!.Status);
        Assert.Equal(1, sync.ItemsFound);
        Assert.Equal(1, sync.ItemsImported);
    }

    [Fact]
    public async Task RerunUpdatesExistingItemsWithoutDuplicating()
    {
        int supplierId;
        {
            var supplier = new Supplier("Acme", "https://acme.test/catalog");
            _context.Suppliers.Add(supplier);
            await _context.SaveChangesAsync();
            supplierId = supplier.Id;
        }

        async Task<int> NewSyncAsync()
        {
            var sync = new CatalogSync(supplierId);
            _context.CatalogSyncs.Add(sync);
            await _context.SaveChangesAsync();
            return sync.Id;
        }

        // First run.
        ScraperReturns(true, Product("A-1", "Alpha", 10.00m));
        await CreateService().RunSyncAsync(await NewSyncAsync());

        // Second run: same external id, changed name/price -> updates the same item.
        // Re-stubbing replaces the configured return for the next call.
        ScraperReturns(true, Product("A-1", "Alpha Renamed", 12.50m));
        await CreateService().RunSyncAsync(await NewSyncAsync());

        Assert.Equal(1, await _context.CatalogItems.CountAsync());
        Assert.Equal(1, await _context.SupplierProductMaps.CountAsync());
        var item = await _context.CatalogItems.SingleAsync();
        Assert.Equal("Alpha Renamed", item.Name);
        Assert.Equal(12.50m, item.Price);
    }

    [Fact]
    public async Task MarksFailedWhenScraperThrows()
    {
        int syncId = await SeedSupplierAndSyncAsync();
        _scraper.ScrapeListingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<SupplierScrapeResult>(_ => throw new InvalidOperationException("provider down"));

        await CreateService().RunSyncAsync(syncId);

        var sync = await _context.CatalogSyncs.FindAsync(syncId);
        Assert.Equal(CatalogSyncStatus.Failed, sync!.Status);
        Assert.Equal("provider down", sync.ErrorMessage);
        Assert.Equal(0, await _context.CatalogItems.CountAsync());
    }
}
