using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class EfRepository<T> : RepositoryBase<T>, IReadRepository<T>, IRepository<T> where T : class, IAggregateRoot
{
    private readonly CatalogContext _dbContext;

    public EfRepository(CatalogContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public override async Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        var entry = _dbContext.Entry(entity);
        if (entry.State == EntityState.Detached)
        {
            _dbContext.Attach(entity);
            entry = _dbContext.Entry(entity);
        }

        entry.State = EntityState.Modified;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
