using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SupplierCatalogSyncServiceTests;

public class ExecuteSyncAsync
{
    private readonly IRepository<CatalogSync> _syncRepo = Substitute.For<IRepository<CatalogSync>>();
    private readonly IRepository<Supplier> _supplierRepo = Substitute.For<IRepository<Supplier>>();
    private readonly IRepository<CatalogItem> _itemRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<CatalogBrand> _brandRepo = Substitute.For<IRepository<CatalogBrand>>();
    private readonly IRepository<CatalogType> _typeRepo = Substitute.For<IRepository<CatalogType>>();
    private readonly IRepository<SupplierCatalogItem> _mapRepo = Substitute.For<IRepository<SupplierCatalogItem>>();
    private readonly IFirecrawlClient _firecrawl = Substitute.For<IFirecrawlClient>();
    private readonly IAppLogger<SupplierCatalogSyncService> _logger = Substitute.For<IAppLogger<SupplierCatalogSyncService>>();

    private SupplierCatalogSyncService CreateService() => new(
        _syncRepo, _supplierRepo, _itemRepo, _brandRepo, _typeRepo, _mapRepo, _firecrawl, _logger);

    public ExecuteSyncAsync()
    {
        var sync = new CatalogSync(1);
        var supplier = WithId(new Supplier("Acme Supplies", "https://acme.example/catalog"), 1);
        _syncRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(sync);
        _supplierRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(supplier);
        _typeRepo.FirstOrDefaultAsync(Arg.Any<CatalogTypeByNameSpecification>(), Arg.Any<CancellationToken>())
            .Returns(WithId(new CatalogType("Imported"), 9));
        _brandRepo.FirstOrDefaultAsync(Arg.Any<CatalogBrandByNameSpecification>(), Arg.Any<CancellationToken>())
            .Returns(WithId(new CatalogBrand("Acme"), 5));
    }

    [Fact]
    public async Task MarksCompletedWhenEveryFoundProductIsImported()
    {
        _firecrawl.ScrapeProductListingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<ScrapedProduct>
            {
                new() { Name = "Widget", Description = "A widget", Price = 9.99m, Brand = "Acme", Sku = "SKU1" },
                new() { Name = "Gadget", Description = "A gadget", Price = 19.99m, Brand = "Acme", Sku = "SKU2" }
            });
        _mapRepo.FirstOrDefaultAsync(Arg.Any<SupplierCatalogItemByExternalIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns((SupplierCatalogItem?)null);
        _itemRepo.AddAsync(Arg.Any<CatalogItem>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(WithId(ci.Arg<CatalogItem>(), 200)));

        CatalogSync captured = null!;
        await _syncRepo.UpdateAsync(Arg.Do<CatalogSync>(s => captured = s), Arg.Any<CancellationToken>());

        await CreateService().ExecuteSyncAsync(1);

        Assert.Equal(SyncStatus.Completed, captured.Status);
        Assert.Equal(2, captured.ItemsFound);
        Assert.Equal(2, captured.ItemsImported);
        await _itemRepo.Received(2).AddAsync(Arg.Any<CatalogItem>(), Arg.Any<CancellationToken>());
        await _mapRepo.Received(2).AddAsync(Arg.Any<SupplierCatalogItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdatesExistingItemInsteadOfCreatingDuplicateWhenAlreadyMapped()
    {
        _firecrawl.ScrapeProductListingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<ScrapedProduct>
            {
                new() { Name = "Widget v2", Description = "Updated", Price = 12.50m, Brand = "Acme", Sku = "SKU1" }
            });

        var existingItem = WithId(new CatalogItem(9, 5, "old", "Widget v1", 9.99m, "pic"), 100);
        var existingMapping = new SupplierCatalogItem(1, "SKU1", 100);
        _mapRepo.FirstOrDefaultAsync(Arg.Any<SupplierCatalogItemByExternalIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(existingMapping);
        _itemRepo.GetByIdAsync(100, Arg.Any<CancellationToken>()).Returns(existingItem);

        CatalogSync captured = null!;
        await _syncRepo.UpdateAsync(Arg.Do<CatalogSync>(s => captured = s), Arg.Any<CancellationToken>());

        await CreateService().ExecuteSyncAsync(1);

        Assert.Equal(SyncStatus.Completed, captured.Status);
        Assert.Equal(1, captured.ItemsImported);
        // The existing catalog item is updated in place; nothing new is created.
        await _itemRepo.Received().UpdateAsync(existingItem, Arg.Any<CancellationToken>());
        await _itemRepo.DidNotReceive().AddAsync(Arg.Any<CatalogItem>(), Arg.Any<CancellationToken>());
        await _mapRepo.DidNotReceive().AddAsync(Arg.Any<SupplierCatalogItem>(), Arg.Any<CancellationToken>());
        Assert.Equal("Widget v2", existingItem.Name);
        Assert.Equal(12.50m, existingItem.Price);
    }

    [Fact]
    public async Task MarksPartiallyCompletedWhenSomeFoundProductsCannotBeImported()
    {
        _firecrawl.ScrapeProductListingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<ScrapedProduct>
            {
                new() { Name = "Priced", Description = "ok", Price = 5m, Brand = "Acme", Sku = "SKU1" },
                new() { Name = "No price", Description = "missing price", Price = null, Brand = "Acme", Sku = "SKU2" }
            });
        _mapRepo.FirstOrDefaultAsync(Arg.Any<SupplierCatalogItemByExternalIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns((SupplierCatalogItem?)null);
        _itemRepo.AddAsync(Arg.Any<CatalogItem>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(WithId(ci.Arg<CatalogItem>(), 201)));

        CatalogSync captured = null!;
        await _syncRepo.UpdateAsync(Arg.Do<CatalogSync>(s => captured = s), Arg.Any<CancellationToken>());

        await CreateService().ExecuteSyncAsync(1);

        Assert.Equal(SyncStatus.PartiallyCompleted, captured.Status);
        Assert.Equal(2, captured.ItemsFound);
        Assert.Equal(1, captured.ItemsImported);
    }

    [Fact]
    public async Task MarksFailedWhenTheListingCannotBeRead()
    {
        _firecrawl.ScrapeProductListingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ScrapedProduct>>(_ => throw new FirecrawlException("boom"));

        CatalogSync captured = null!;
        await _syncRepo.UpdateAsync(Arg.Do<CatalogSync>(s => captured = s), Arg.Any<CancellationToken>());

        await CreateService().ExecuteSyncAsync(1);

        Assert.Equal(SyncStatus.Failed, captured.Status);
        Assert.Equal("boom", captured.ErrorMessage);
        await _itemRepo.DidNotReceive().AddAsync(Arg.Any<CatalogItem>(), Arg.Any<CancellationToken>());
    }

    // BaseEntity.Id has a protected setter; set its backing field so mocked repositories can hand
    // back entities that already have an identity, as they would after a real SaveChanges.
    private static T WithId<T>(T entity, int id) where T : BaseEntity
    {
        var field = typeof(BaseEntity).GetField("<Id>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(entity, id);
        return entity;
    }
}
