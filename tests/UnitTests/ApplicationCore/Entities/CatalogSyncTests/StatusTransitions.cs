using Microsoft.eShopWeb.ApplicationCore.Entities;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.CatalogSyncTests;

public class StatusTransitions
{
    [Fact]
    public void StartsRunning()
    {
        var sync = new CatalogSync(1);

        Assert.Equal(SyncStatus.Running, sync.Status);
        Assert.Equal(0, sync.ItemsFound);
        Assert.Equal(0, sync.ItemsImported);
        Assert.Null(sync.CompletedAt);
    }

    [Fact]
    public void MarkCompleted_IsCompleted_WhenEveryFoundItemImported()
    {
        var sync = new CatalogSync(1);

        sync.MarkCompleted(itemsFound: 16, itemsImported: 16);

        Assert.Equal(SyncStatus.Completed, sync.Status);
        Assert.Equal(16, sync.ItemsFound);
        Assert.Equal(16, sync.ItemsImported);
        Assert.NotNull(sync.CompletedAt);
    }

    [Fact]
    public void MarkCompleted_IsPartiallyCompleted_WhenSomeItemsNotImported()
    {
        var sync = new CatalogSync(1);

        sync.MarkCompleted(itemsFound: 16, itemsImported: 15);

        Assert.Equal(SyncStatus.PartiallyCompleted, sync.Status);
        Assert.Equal(16, sync.ItemsFound);
        Assert.Equal(15, sync.ItemsImported);
    }

    [Fact]
    public void MarkCompleted_IsCompleted_WhenListingIsEmpty()
    {
        var sync = new CatalogSync(1);

        sync.MarkCompleted(itemsFound: 0, itemsImported: 0);

        Assert.Equal(SyncStatus.Completed, sync.Status);
    }

    [Fact]
    public void MarkFailed_RecordsError()
    {
        var sync = new CatalogSync(1);

        sync.MarkFailed("boom");

        Assert.Equal(SyncStatus.Failed, sync.Status);
        Assert.Equal("boom", sync.Error);
        Assert.NotNull(sync.CompletedAt);
    }
}
