using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Services;

public class SupplierCatalogSyncServiceTests
{
    private readonly CatalogContext _context;
    private readonly IFirecrawlProductScraper _scraper = Substitute.For<IFirecrawlProductScraper>();
    private readonly ISupplierSyncQueue _queue = Substitute.For<ISupplierSyncQueue>();
    private readonly SupplierCatalogSyncService _service;

    public SupplierCatalogSyncServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase($"SyncTests-{Guid.NewGuid()}")
            .Options;
        _context = new CatalogContext(options);

        var settings = Options.Create(new FirecrawlSettings { PollIntervalSeconds = 1, PollTimeoutSeconds = 5 });

        _service = new SupplierCatalogSyncService(
            new EfRepository<Supplier>(_context),
            new EfRepository<CatalogSync>(_context),
            new EfRepository<CatalogItem>(_context),
            new EfRepository<CatalogBrand>(_context),
            new EfRepository<CatalogType>(_context),
            _scraper,
            _queue,
            Substitute.For<IAppLogger<SupplierCatalogSyncService>>(),
            settings);
    }

    private void SetExtraction(params ScrapedProduct[] products)
    {
        _scraper.StartExtractionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("job-1");
        _scraper.GetExtractionAsync("job-1", Arg.Any<CancellationToken>())
            .Returns(new ProductExtractionResult(ExtractionState.Completed, products));
    }

    private static ScrapedProduct Product(string? id, string name, string? desc, decimal? price, string? brand)
        => new(id, name, desc, price, brand);

    [Fact]
    public async Task ImportsProducts_CreatesItemsBrandsAndType()
    {
        SetExtraction(
            Product("SKU-1", "Widget A", "Desc A", 10m, "Acme"),
            Product("SKU-2", "Widget B", null, 20m, "Acme"));

        var supplier = await _service.RegisterSupplierAsync("Acme Co", "https://acme.test/catalog");
        var sync = await _service.StartSyncAsync(supplier.Id);
        Assert.NotNull(sync);

        await _service.RunSyncAsync(sync!.Id);

        var finished = await _service.GetSyncAsync(sync.Id);
        Assert.Equal(SyncStatus.Completed, finished!.Status);
        Assert.Equal(2, finished.ItemsFound);
        Assert.Equal(2, finished.ItemsImported);

        var items = await _context.CatalogItems.ToListAsync();
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal(supplier.Id, i.SupplierId));

        // Description falls back to the name when the supplier provides none.
        var b = items.Single(i => i.Name == "Widget B");
        Assert.Equal("Widget B", b.Description);

        // The brand was created once and reused for both items.
        Assert.Single(await _context.CatalogBrands.Where(x => x.Brand == "Acme").ToListAsync());
        // A single "Imported" catalog type was created for imported products.
        Assert.Single(await _context.CatalogTypes.Where(x => x.Type == "Imported").ToListAsync());
    }

    [Fact]
    public async Task ProductWithoutUsablePrice_IsFoundButNotImported_Partial()
    {
        SetExtraction(
            Product("SKU-1", "Priced", "d", 10m, "Acme"),
            Product("SKU-2", "No Price", "d", null, "Beta"));

        var supplier = await _service.RegisterSupplierAsync("Acme Co", "https://acme.test/catalog");
        var sync = await _service.StartSyncAsync(supplier.Id);
        await _service.RunSyncAsync(sync!.Id);

        var finished = await _service.GetSyncAsync(sync.Id);
        Assert.Equal(SyncStatus.PartiallyCompleted, finished!.Status);
        Assert.Equal(2, finished.ItemsFound);
        Assert.Equal(1, finished.ItemsImported);

        Assert.Single(await _context.CatalogItems.ToListAsync());
        // The brand of a skipped product is never created.
        Assert.Empty(await _context.CatalogBrands.Where(x => x.Brand == "Beta").ToListAsync());
    }

    [Fact]
    public async Task ReRunningSync_UpdatesExistingItems_NoDuplicates()
    {
        SetExtraction(Product("SKU-1", "Widget A", "Desc A", 10m, "Acme"));

        var supplier = await _service.RegisterSupplierAsync("Acme Co", "https://acme.test/catalog");

        var first = await _service.StartSyncAsync(supplier.Id);
        await _service.RunSyncAsync(first!.Id);
        Assert.Single(await _context.CatalogItems.ToListAsync());

        // Same supplier item key, but a changed price on a second run.
        SetExtraction(Product("SKU-1", "Widget A (v2)", "Desc A v2", 15m, "Acme"));
        var second = await _service.StartSyncAsync(supplier.Id);
        await _service.RunSyncAsync(second!.Id);

        var items = await _context.CatalogItems.ToListAsync();
        Assert.Single(items);                       // still one item — updated, not duplicated
        Assert.Equal(15m, items[0].Price);
        Assert.Equal("Widget A (v2)", items[0].Name);
    }

    [Fact]
    public async Task StartSync_UnknownSupplier_ReturnsNull()
    {
        Assert.Null(await _service.StartSyncAsync(supplierId: 424242));
    }

    [Fact]
    public async Task FailedExtraction_MarksSyncFailed()
    {
        _scraper.StartExtractionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("job-1");
        _scraper.GetExtractionAsync("job-1", Arg.Any<CancellationToken>())
            .Returns(new ProductExtractionResult(ExtractionState.Failed, Array.Empty<ScrapedProduct>(), "boom"));

        var supplier = await _service.RegisterSupplierAsync("Acme Co", "https://acme.test/catalog");
        var sync = await _service.StartSyncAsync(supplier.Id);
        await _service.RunSyncAsync(sync!.Id);

        var finished = await _service.GetSyncAsync(sync.Id);
        Assert.Equal(SyncStatus.Failed, finished!.Status);
        Assert.Contains("boom", finished.ErrorMessage);
    }
}
