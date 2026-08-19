using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.CatalogSyncTests;

public class CatalogSyncStatus
{
    private const int SupplierId = 7;

    [Fact]
    public void StartsRunning()
    {
        var sync = new CatalogSync(SupplierId);

        Assert.Equal(SyncStatus.Running, sync.Status);
        Assert.Equal(SupplierId, sync.SupplierId);
        Assert.Null(sync.CompletedAt);
    }

    [Fact]
    public void IsCompletedWhenEveryFoundProductImported()
    {
        var sync = new CatalogSync(SupplierId);

        sync.MarkFinished(itemsFound: 20, itemsImported: 20);

        Assert.Equal(SyncStatus.Completed, sync.Status);
        Assert.Equal(20, sync.ItemsFound);
        Assert.Equal(20, sync.ItemsImported);
        Assert.NotNull(sync.CompletedAt);
    }

    [Fact]
    public void IsPartialWhenOnlySomeFoundProductsImported()
    {
        var sync = new CatalogSync(SupplierId);

        sync.MarkFinished(itemsFound: 20, itemsImported: 17);

        Assert.Equal(SyncStatus.Partial, sync.Status);
        Assert.Equal(20, sync.ItemsFound);
        Assert.Equal(17, sync.ItemsImported);
    }

    [Fact]
    public void IsCompletedWhenListingHadNoProducts()
    {
        var sync = new CatalogSync(SupplierId);

        sync.MarkFinished(itemsFound: 0, itemsImported: 0);

        Assert.Equal(SyncStatus.Completed, sync.Status);
    }

    [Fact]
    public void IsFailedWhenListingCouldNotBeRead()
    {
        var sync = new CatalogSync(SupplierId);

        sync.MarkFailed("listing unreachable");

        Assert.Equal(SyncStatus.Failed, sync.Status);
        Assert.Equal("listing unreachable", sync.ErrorMessage);
        Assert.NotNull(sync.CompletedAt);
    }
}
