using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationCommands
{
    Task PersistDisposalAsync(int notificationId, CancellationToken cancellationToken = default);
}
