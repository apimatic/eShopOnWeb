using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.SupplierSyncTests;

public class SupplierSyncStatus
{
    private const int TestSupplierId = 7;

    [Fact]
    public void StartsQueued()
    {
        var sync = new SupplierSync(TestSupplierId);

        Assert.Equal(SyncStatus.Queued, sync.Status);
        Assert.Equal(0, sync.ItemsFound);
        Assert.Equal(0, sync.ItemsImported);
        Assert.Null(sync.StartedAt);
        Assert.Null(sync.CompletedAt);
    }

    [Fact]
    public void MarkRunningSetsRunningAndStartTime()
    {
        var sync = new SupplierSync(TestSupplierId);

        sync.MarkRunning();

        Assert.Equal(SyncStatus.Running, sync.Status);
        Assert.NotNull(sync.StartedAt);
    }

    [Fact]
    public void CompletedWhenEverythingFoundIsImportedAndListingFullyCaptured()
    {
        var sync = new SupplierSync(TestSupplierId);
        sync.MarkRunning();

        sync.MarkCompleted(itemsFound: 20, itemsImported: 20, listingFullyCaptured: true);

        Assert.Equal(SyncStatus.Completed, sync.Status);
        Assert.Equal(20, sync.ItemsFound);
        Assert.Equal(20, sync.ItemsImported);
        Assert.NotNull(sync.CompletedAt);
    }

    [Fact]
    public void PartiallyCompletedWhenSomeFoundItemsWereNotImported()
    {
        var sync = new SupplierSync(TestSupplierId);
        sync.MarkRunning();

        sync.MarkCompleted(itemsFound: 20, itemsImported: 18, listingFullyCaptured: true);

        Assert.Equal(SyncStatus.PartiallyCompleted, sync.Status);
    }

    [Fact]
    public void PartiallyCompletedWhenListingWasNotFullyCaptured()
    {
        var sync = new SupplierSync(TestSupplierId);
        sync.MarkRunning();

        sync.MarkCompleted(itemsFound: 20, itemsImported: 20, listingFullyCaptured: false);

        Assert.Equal(SyncStatus.PartiallyCompleted, sync.Status);
    }

    [Fact]
    public void FailedCapturesErrorMessage()
    {
        var sync = new SupplierSync(TestSupplierId);
        sync.MarkRunning();

        sync.MarkFailed("boom");

        Assert.Equal(SyncStatus.Failed, sync.Status);
        Assert.Equal("boom", sync.ErrorMessage);
        Assert.NotNull(sync.CompletedAt);
    }
}
