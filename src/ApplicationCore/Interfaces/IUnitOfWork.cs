using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Commits the changes already made to tracked aggregate instances. Repository
/// UpdateAsync deep-marks the entity (which fights owned-entity keys); aggregate
/// state transitions in this app persist through the change tracker instead.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
