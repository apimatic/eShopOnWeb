using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SupplierServiceTests;

public class StartSyncAsync
{
    private readonly IRepository<Supplier> _supplierRepo = Substitute.For<IRepository<Supplier>>();
    private readonly IRepository<CatalogSync> _syncRepo = Substitute.For<IRepository<CatalogSync>>();
    private readonly ISyncJobQueue _queue = Substitute.For<ISyncJobQueue>();
    private readonly IAppLogger<SupplierService> _logger = Substitute.For<IAppLogger<SupplierService>>();

    private SupplierService CreateService() => new(_supplierRepo, _syncRepo, _queue, _logger);

    [Fact]
    public async Task ThrowsSupplierNotFound_WhenSupplierDoesNotExist()
    {
        _supplierRepo.GetByIdAsync(42, Arg.Any<CancellationToken>()).Returns((Supplier?)null);

        await Assert.ThrowsAsync<SupplierNotFoundException>(() => CreateService().StartSyncAsync(42));
        _queue.DidNotReceive().Enqueue(Arg.Any<int>());
    }

    [Fact]
    public async Task CreatesRunningSyncAndEnqueuesIt_WhenSupplierExists()
    {
        var supplier = Substitute.For<Supplier>("Test", "https://supplier.example/");
        supplier.Id.Returns(1);
        _supplierRepo.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(supplier);

        var persisted = new CatalogSync(1);
        _syncRepo.AddAsync(Arg.Any<CatalogSync>(), Arg.Any<CancellationToken>()).Returns(persisted);

        var result = await CreateService().StartSyncAsync(1);

        Assert.Same(persisted, result);
        Assert.Equal(SyncStatus.Running, result.Status);
        await _syncRepo.Received().AddAsync(Arg.Is<CatalogSync>(s => s.SupplierId == 1), Arg.Any<CancellationToken>());
        _queue.Received().Enqueue(persisted.Id);
    }
}
