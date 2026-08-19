using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.SupplierAggregate;

public class CatalogSyncTests
{
    private readonly Guid _supplierId = Guid.NewGuid();

    [Fact]
    public void StartsPending()
    {
        var sync = new CatalogSync(_supplierId);

        Assert.Equal(SyncStatus.Pending, sync.Status);
        Assert.Equal(_supplierId, sync.SupplierId);
        Assert.Null(sync.StartedAt);
        Assert.Null(sync.CompletedAt);
    }

    [Fact]
    public void MarkRunningSetsStartedAt()
    {
        var sync = new CatalogSync(_supplierId);

        sync.MarkRunning();

        Assert.Equal(SyncStatus.Running, sync.Status);
        Assert.NotNull(sync.StartedAt);
    }

    [Fact]
    public void CompleteWithAllImportedIsCompleted()
    {
        var sync = new CatalogSync(_supplierId);

        sync.Complete(itemsFound: 16, itemsImported: 16);

        Assert.Equal(SyncStatus.Completed, sync.Status);
        Assert.Equal(16, sync.ItemsFound);
        Assert.Equal(16, sync.ItemsImported);
        Assert.NotNull(sync.CompletedAt);
    }

    [Fact]
    public void CompleteWithShortfallIsPartiallyImported()
    {
        var sync = new CatalogSync(_supplierId);

        sync.Complete(itemsFound: 16, itemsImported: 15, detail: "1 could not be imported");

        Assert.Equal(SyncStatus.PartiallyImported, sync.Status);
        Assert.Equal(16, sync.ItemsFound);
        Assert.Equal(15, sync.ItemsImported);
        Assert.Equal("1 could not be imported", sync.Detail);
    }

    [Fact]
    public void FailSetsFailedStatus()
    {
        var sync = new CatalogSync(_supplierId);

        sync.Fail("listing unreachable");

        Assert.Equal(SyncStatus.Failed, sync.Status);
        Assert.Equal("listing unreachable", sync.Detail);
        Assert.NotNull(sync.CompletedAt);
    }
}
