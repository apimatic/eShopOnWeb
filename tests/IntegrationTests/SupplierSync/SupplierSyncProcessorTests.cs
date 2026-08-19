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
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.SupplierSyncTests;

public class SupplierSyncProcessorTests
{
    private readonly CatalogContext _context;
    private readonly EfRepository<SupplierSync> _syncRepo;
    private readonly EfRepository<Supplier> _supplierRepo;
    private readonly EfRepository<CatalogItem> _itemRepo;
    private readonly EfRepository<CatalogBrand> _brandRepo;
    private readonly EfRepository<CatalogType> _typeRepo;
    private readonly EfRepository<SupplierCatalogItem> _mapRepo;

    public SupplierSyncProcessorTests()
    {
        // A unique database per test instance keeps runs isolated.
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName: "SupplierSyncProcessorTests_" + System.Guid.NewGuid())
            .Options;
        _context = new CatalogContext(options);
        _syncRepo = new EfRepository<SupplierSync>(_context);
        _supplierRepo = new EfRepository<Supplier>(_context);
        _itemRepo = new EfRepository<CatalogItem>(_context);
        _brandRepo = new EfRepository<CatalogBrand>(_context);
        _typeRepo = new EfRepository<CatalogType>(_context);
        _mapRepo = new EfRepository<SupplierCatalogItem>(_context);
    }

    private SupplierSyncProcessor CreateProcessor(ISupplierCatalogReader reader) =>
        new(_syncRepo, _supplierRepo, _itemRepo, _brandRepo, _typeRepo, _mapRepo, reader, new NullAppLogger<SupplierSyncProcessor>());

    private async Task<(int supplierId, int syncId)> SeedSupplierAndSyncAsync()
    {
        var supplier = await _supplierRepo.AddAsync(new Supplier("Acme", "https://acme.example/catalog"));
        var sync = await _syncRepo.AddAsync(new SupplierSync(supplier.Id));
        return (supplier.Id, sync.Id);
    }

    [Fact]
    public async Task ImportsAllProducts_AndReportsCompleted_WhenListingFullyCaptured()
    {
        var (_, syncId) = await SeedSupplierAndSyncAsync();
        var reader = new StubReader(new SupplierListingReadResult(new[]
        {
            new ScrapedProduct { ExternalId = "https://acme.example/p/1", Name = "Widget", Description = "A widget", Price = 9.99m, Brand = "Acme" },
            new ScrapedProduct { ExternalId = "https://acme.example/p/2", Name = "Gadget", Description = "A gadget", Price = 19.5m, Brand = "Acme" }
        }, listingFullyCaptured: true));

        await CreateProcessor(reader).ProcessAsync(syncId);

        var sync = await _syncRepo.GetByIdAsync(syncId);
        Assert.Equal(SyncStatus.Completed, sync!.Status);
        Assert.Equal(2, sync.ItemsFound);
        Assert.Equal(2, sync.ItemsImported);
        Assert.Equal(2, (await _itemRepo.ListAsync()).Count);
    }

    [Fact]
    public async Task SkipsUnusableProducts_AndReportsPartiallyCompleted()
    {
        var (_, syncId) = await SeedSupplierAndSyncAsync();
        var reader = new StubReader(new SupplierListingReadResult(new[]
        {
            new ScrapedProduct { ExternalId = "https://acme.example/p/1", Name = "Widget", Description = "A widget", Price = 9.99m, Brand = "Acme" },
            new ScrapedProduct { ExternalId = "https://acme.example/p/2", Name = "No Price", Description = "missing price", Price = null, Brand = "Acme" }
        }, listingFullyCaptured: true));

        await CreateProcessor(reader).ProcessAsync(syncId);

        var sync = await _syncRepo.GetByIdAsync(syncId);
        Assert.Equal(SyncStatus.PartiallyCompleted, sync!.Status);
        Assert.Equal(2, sync.ItemsFound);
        Assert.Equal(1, sync.ItemsImported);
        Assert.Single(await _itemRepo.ListAsync());
    }

    [Fact]
    public async Task ReSync_UpdatesExistingItems_WithoutDuplicating()
    {
        var (supplierId, firstSyncId) = await SeedSupplierAndSyncAsync();
        var products = new[]
        {
            new ScrapedProduct { ExternalId = "https://acme.example/p/1", Name = "Widget", Description = "A widget", Price = 9.99m, Brand = "Acme" },
            new ScrapedProduct { ExternalId = "https://acme.example/p/2", Name = "Gadget", Description = "A gadget", Price = 19.5m, Brand = "Acme" }
        };
        await CreateProcessor(new StubReader(new SupplierListingReadResult(products, true))).ProcessAsync(firstSyncId);

        // Second run: same external ids, but the first product's price/name changed at the source.
        var updated = new[]
        {
            new ScrapedProduct { ExternalId = "https://acme.example/p/1", Name = "Widget Deluxe", Description = "A better widget", Price = 12.49m, Brand = "Acme" },
            new ScrapedProduct { ExternalId = "https://acme.example/p/2", Name = "Gadget", Description = "A gadget", Price = 19.5m, Brand = "Acme" }
        };
        var secondSync = await _syncRepo.AddAsync(new SupplierSync(supplierId));
        await CreateProcessor(new StubReader(new SupplierListingReadResult(updated, true))).ProcessAsync(secondSync.Id);

        // Still exactly two catalog items and two mappings — no duplicates.
        var items = await _itemRepo.ListAsync();
        Assert.Equal(2, items.Count);
        Assert.Equal(2, (await _mapRepo.ListAsync()).Count);

        // The changed product was updated in place.
        var deluxe = items.Single(i => i.Name == "Widget Deluxe");
        Assert.Equal(12.49m, deluxe.Price);
    }

    private sealed class StubReader : ISupplierCatalogReader
    {
        private readonly SupplierListingReadResult _result;
        public StubReader(SupplierListingReadResult result) => _result = result;
        public Task<SupplierListingReadResult> ReadListingAsync(string listingUrl, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private sealed class NullAppLogger<T> : IAppLogger<T>
    {
        public void LogInformation(string message, params object[] args) { }
        public void LogWarning(string message, params object[] args) { }
    }
}
