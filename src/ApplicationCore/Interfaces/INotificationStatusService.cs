using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Refreshes the stored delivery outcome of notifications from the provider. There is no
/// publicly reachable URL for this application, so delivery state is obtained by asking the
/// provider rather than by receiving callbacks from it.
/// </summary>
public interface INotificationStatusService
{
    Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default);
}
