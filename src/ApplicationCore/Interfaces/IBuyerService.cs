using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IBuyerService
{
    /// <summary>
    /// Returns the <see cref="Buyer"/> for the given identity (the JWT-authenticated user), creating
    /// and persisting one on first use. The returned buyer has its <see cref="Buyer.PaymentMethods"/>
    /// loaded so saved cards can be inspected.
    /// </summary>
    Task<Buyer> GetOrCreateBuyerAsync(string identityGuid, CancellationToken cancellationToken = default);
}
