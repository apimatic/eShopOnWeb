using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBillingAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionReservationStore
{
    Task<(SubscriptionReservation Reservation, bool Created)> GetOrCreateAsync(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}
