using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Resolves the authenticated user name to the eShopOnWeb account behind it.</summary>
public interface IShopperDirectory
{
    Task<Shopper?> FindByUserNameAsync(string userName, CancellationToken cancellationToken);
}
