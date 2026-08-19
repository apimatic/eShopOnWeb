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
/// Exercises the sync orchestration against an in-memory catalog with a stubbed product reader,
/// locking in the import, idempotency and found-vs-imported/status behavior.
/// </summary>
public class SupplierCatalogSyncServiceTests
{
    private readonly CatalogContext _context;
    private readonly EfRepository<Supplier> _supplierRepository;
    private readonly EfRepository<SupplierSync> _syncRepository;
    private readonly EfRepository<SupplierCatalogItem> _mappingRepository;
    private readonly EfRepository<CatalogItem> _catalogItemRepository;
    private readonly EfRepository<CatalogBrand> _brandRepository;
    private readonly EfRepository<CatalogType> _typeRepository;
    private readonly ISupplierProductReader _reader = Substitute.For<ISupplierProductReader>();
    private readonly ISupplierSyncQueue _queue = Substitute.For<ISupplierSyncQueue>();
    private readonly IAppLogger<SupplierCatalogSyncService> _logger = Substitute.For<IAppLogger<SupplierCatalogSyncService>>();

    public SupplierCatalogSyncServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName: "SupplierSyncTests_" + System.Guid.NewGuid())
            .Options;
        _context = new CatalogContext(options);
        _supplierRepository = new EfRepository<Supplier>(_context);
        _syncRepository = new EfRepository<SupplierSync>(_context);
        _mappingRepository = new EfRepository<SupplierCatalogItem>(_context);
        _catalogItemRepository = new EfRepository<CatalogItem>(_context);
        _brandRepository = new EfRepository<CatalogBrand>(_context);
        _typeRepository = new EfRepository<CatalogType>(_context);
    }

    private SupplierCatalogSyncService CreateService() => new(
        _supplierRepository, _syncRepository, _mappingRepository, _catalogItemRepository,
        _brandRepository, _typeRepository, _reader, _queue, _logger);

    private void ReaderReturns(bool fullyCaptured, params SupplierProduct[] products) =>
        _reader.ReadProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SupplierProductReadResult(products, fullyCaptured)));

    private async Task<(int supplierId, int syncId)> SeedSupplierAndSyncAsync()
    {
        var supplier = await _supplierRepository.AddAsync(new Supplier("Acme", "https://acme.example/products"));
        var sync = await _syncRepository.AddAsync(new SupplierSync(supplier.Id));
        return (supplier.Id, sync.Id);
    }

    [Fact]
    public async Task ImportsAllProducts_WhenListingFullyCaptured()
    {
        ReaderReturns(true,
            new SupplierProduct("https://acme.example/p1", "Widget", "A widget", 9.99m, "Acme", "https://img/1.png"),
            new SupplierProduct("https://acme.example/p2", "Gadget", null, 19.5m, null, null));
        var (_, syncId) = await SeedSupplierAndSyncAsync();

        await CreateService().RunSyncAsync(syncId);

        var sync = await _syncRepository.GetByIdAsync(syncId);
        Assert.Equal(SyncStatus.Completed, sync!.Status);
        Assert.Equal(2, sync.ItemsFound);
        Assert.Equal(2, sync.ItemsImported);
        Assert.Equal(2, await _catalogItemRepository.CountAsync());
        Assert.Equal(2, await _mappingRepository.CountAsync());
    }

    [Fact]
    public async Task ReSync_IsIdempotent_AndUpdatesInPlace()
    {
        ReaderReturns(true,
            new SupplierProduct("https://acme.example/p1", "Widget", "A widget", 9.99m, "Acme", null));
        var (_, syncId1) = await SeedSupplierAndSyncAsync();
        await CreateService().RunSyncAsync(syncId1);

        // Same product key, changed price/name — a second sync must update, not duplicate.
        ReaderReturns(true,
            new SupplierProduct("https://acme.example/p1", "Widget Pro", "A better widget", 14.99m, "Acme", null));
        var sync2 = await _syncRepository.AddAsync(new SupplierSync((await _supplierRepository.ListAsync()).First().Id));
        await CreateService().RunSyncAsync(sync2.Id);

        Assert.Equal(1, await _catalogItemRepository.CountAsync());
        Assert.Equal(1, await _mappingRepository.CountAsync());
        var item = (await _catalogItemRepository.ListAsync()).Single();
        Assert.Equal("Widget Pro", item.Name);
        Assert.Equal(14.99m, item.Price);
    }

    [Fact]
    public async Task PartiallyCompleted_WhenSomeProductsMissingRequiredData()
    {
        ReaderReturns(true,
            new SupplierProduct("https://acme.example/p1", "Widget", "desc", 9.99m, "Acme", null),
            new SupplierProduct("https://acme.example/p2", "No price", "desc", null, "Acme", null),
            new SupplierProduct("https://acme.example/p3", "", "desc", 5m, "Acme", null));
        var (_, syncId) = await SeedSupplierAndSyncAsync();

        await CreateService().RunSyncAsync(syncId);

        var sync = await _syncRepository.GetByIdAsync(syncId);
        Assert.Equal(SyncStatus.PartiallyCompleted, sync!.Status);
        Assert.Equal(3, sync.ItemsFound);
        Assert.Equal(1, sync.ItemsImported);
        Assert.False(string.IsNullOrWhiteSpace(sync.StatusDetail));
    }

    [Fact]
    public async Task PartiallyCompleted_WhenListingNotFullyCaptured()
    {
        ReaderReturns(false,
            new SupplierProduct("https://acme.example/p1", "Widget", "desc", 9.99m, "Acme", null));
        var (_, syncId) = await SeedSupplierAndSyncAsync();

        await CreateService().RunSyncAsync(syncId);

        var sync = await _syncRepository.GetByIdAsync(syncId);
        Assert.Equal(SyncStatus.PartiallyCompleted, sync!.Status);
        Assert.Equal(1, sync.ItemsFound);
        Assert.Equal(1, sync.ItemsImported);
    }

    [Fact]
    public async Task Failed_WhenReaderThrows()
    {
        _reader.ReadProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SupplierProductReadResult>(new System.InvalidOperationException("boom")));
        var (_, syncId) = await SeedSupplierAndSyncAsync();

        await CreateService().RunSyncAsync(syncId);

        var sync = await _syncRepository.GetByIdAsync(syncId);
        Assert.Equal(SyncStatus.Failed, sync!.Status);
        Assert.Contains("boom", sync.StatusDetail);
    }

    [Fact]
    public async Task DedupesRepeatedProductsWithinOneRead()
    {
        ReaderReturns(true,
            new SupplierProduct("https://acme.example/p1", "Widget", "desc", 9.99m, "Acme", null),
            new SupplierProduct("https://acme.example/p1", "Widget dup", "desc", 9.99m, "Acme", null));
        var (_, syncId) = await SeedSupplierAndSyncAsync();

        await CreateService().RunSyncAsync(syncId);

        var sync = await _syncRepository.GetByIdAsync(syncId);
        Assert.Equal(1, sync!.ItemsFound);
        Assert.Equal(1, sync.ItemsImported);
        Assert.Equal(SyncStatus.Completed, sync.Status);
        Assert.Equal(1, await _catalogItemRepository.CountAsync());
    }
}
