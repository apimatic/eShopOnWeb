using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class EfUnitOfWork : IUnitOfWork
{
    private readonly CatalogContext _catalogContext;

    public EfUnitOfWork(CatalogContext catalogContext)
    {
        _catalogContext = catalogContext;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _catalogContext.SaveChangesAsync(cancellationToken);
    }
}
