using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SupplierSyncProcessorTests;

public class ProcessAsync
{
    private const int SyncId = 1;
    private const int SupplierId = 1;

    private readonly IRepository<CatalogSync> _syncRepo = Substitute.For<IRepository<CatalogSync>>();
    private readonly IReadRepository<Supplier> _supplierRepo = Substitute.For<IReadRepository<Supplier>>();
    private readonly IRepository<CatalogItem> _itemRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<CatalogBrand> _brandRepo = Substitute.For<IRepository<CatalogBrand>>();
    private readonly IRepository<CatalogType> _typeRepo = Substitute.For<IRepository<CatalogType>>();
    private readonly ISupplierCatalogReader _reader = Substitute.For<ISupplierCatalogReader>();
    private readonly IAppLogger<SupplierSyncProcessor> _logger = Substitute.For<IAppLogger<SupplierSyncProcessor>>();

    private readonly CatalogSync _sync = new(SupplierId);

    private SupplierSyncProcessor CreateProcessor() => new(
        _syncRepo, _supplierRepo, _itemRepo, _brandRepo, _typeRepo, _reader, _logger);

    public ProcessAsync()
    {
        _syncRepo.GetByIdAsync(SyncId, Arg.Any<CancellationToken>()).Returns(_sync);

        var supplier = Substitute.For<Supplier>("Test", "https://supplier.example/");
        supplier.Id.Returns(SupplierId);
        _supplierRepo.GetByIdAsync(SupplierId, Arg.Any<CancellationToken>()).Returns(supplier);

        // Brands/types are found-or-created; here they are always "created" and come back with ids.
        var brand = Substitute.For<CatalogBrand>("BrandX");
        brand.Id.Returns(6);
        _brandRepo.FirstOrDefaultAsync(Arg.Any<CatalogBrandByNameSpecification>(), Arg.Any<CancellationToken>())
            .Returns((CatalogBrand?)null);
        _brandRepo.AddAsync(Arg.Any<CatalogBrand>(), Arg.Any<CancellationToken>()).Returns(brand);

        var type = Substitute.For<CatalogType>("Imported");
        type.Id.Returns(5);
        _typeRepo.FirstOrDefaultAsync(Arg.Any<CatalogTypeByNameSpecification>(), Arg.Any<CancellationToken>())
            .Returns((CatalogType?)null);
        _typeRepo.AddAsync(Arg.Any<CatalogType>(), Arg.Any<CancellationToken>()).Returns(type);

        _itemRepo.AddAsync(Arg.Any<CatalogItem>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<CatalogItem>());
    }

    private void ReaderReturns(params SupplierProduct[] products) =>
        _reader.ReadProductListingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<SupplierProduct>>(products);

    private static SupplierProduct Product(string key, string name, decimal? price, string brand = "BrandX") =>
        new(name, $"{name} description", price, brand, key);

    [Fact]
    public async Task MarksCompleted_WhenEveryFoundProductIsImported()
    {
        ReaderReturns(
            Product("SKU-1", "Chair", 189.99m),
            Product("SKU-2", "Desk", 349m));
        _itemRepo.FirstOrDefaultAsync(Arg.Any<CatalogItemsBySupplierKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((CatalogItem?)null);

        await CreateProcessor().ProcessAsync(SyncId);

        Assert.Equal(2, _sync.ItemsFound);
        Assert.Equal(2, _sync.ItemsImported);
        Assert.Equal(SyncStatus.Completed, _sync.Status);
        await _itemRepo.Received(2).AddAsync(Arg.Any<CatalogItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarksPartiallyCompleted_WhenSomeProductsCannotBeImported()
    {
        // Second product has no price and cannot become a valid catalog item.
        ReaderReturns(
            Product("SKU-1", "Chair", 189.99m),
            Product("SKU-2", "Contact-for-pricing item", null),
            Product("SKU-3", "Desk", 349m));
        _itemRepo.FirstOrDefaultAsync(Arg.Any<CatalogItemsBySupplierKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((CatalogItem?)null);

        await CreateProcessor().ProcessAsync(SyncId);

        Assert.Equal(3, _sync.ItemsFound);
        Assert.Equal(2, _sync.ItemsImported);
        Assert.Equal(SyncStatus.PartiallyCompleted, _sync.Status);
        await _itemRepo.Received(2).AddAsync(Arg.Any<CatalogItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdatesExistingItem_AndDoesNotCreateDuplicate_WhenSupplierKeyMatches()
    {
        var existing = new CatalogItem(5, 6, "Old description", "Old Name", 1m, string.Empty);
        existing.LinkToSupplier(SupplierId, "SKU-1");
        _itemRepo.FirstOrDefaultAsync(Arg.Any<CatalogItemsBySupplierKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        ReaderReturns(Product("SKU-1", "Chair", 189.99m));

        await CreateProcessor().ProcessAsync(SyncId);

        Assert.Equal(1, _sync.ItemsFound);
        Assert.Equal(1, _sync.ItemsImported);
        Assert.Equal(SyncStatus.Completed, _sync.Status);
        await _itemRepo.Received(1).UpdateAsync(existing, Arg.Any<CancellationToken>());
        await _itemRepo.DidNotReceive().AddAsync(Arg.Any<CatalogItem>(), Arg.Any<CancellationToken>());
        Assert.Equal("Chair", existing.Name);
        Assert.Equal(189.99m, existing.Price);
    }

    [Fact]
    public async Task MarksFailed_WhenListingCannotBeRead()
    {
        _reader.ReadProductListingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<SupplierProduct>>(_ => throw new SupplierCatalogReadException("Firecrawl unavailable"));

        await CreateProcessor().ProcessAsync(SyncId);

        Assert.Equal(SyncStatus.Failed, _sync.Status);
        Assert.Equal("Firecrawl unavailable", _sync.Error);
        await _syncRepo.Received().UpdateAsync(_sync, Arg.Any<CancellationToken>());
    }
}
