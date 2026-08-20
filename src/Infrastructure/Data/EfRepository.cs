using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class EfRepository<T> : RepositoryBase<T>, IReadRepository<T>, IRepository<T> where T : class, IAggregateRoot
{
    private readonly CatalogContext _catalogContext;

    public EfRepository(CatalogContext dbContext) : base(dbContext)
    {
        _catalogContext = dbContext;
    }

    public override async Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        var entry = _catalogContext.Entry(entity);
        if (entry.State == EntityState.Detached)
        {
            _catalogContext.Set<T>().Attach(entity);
            entry.State = EntityState.Modified;
            foreach (var reference in entry.References)
            {
                if (reference.TargetEntry?.Metadata.IsOwned() == true
                    && reference.TargetEntry.State == EntityState.Detached)
                {
                    reference.TargetEntry.State = EntityState.Unchanged;
                }
            }
        }

        await _catalogContext.SaveChangesAsync(cancellationToken);
    }
}
