using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface INotificationPersistence
{
    Task MarkContentRedactedAsync(int notificationId, CancellationToken cancellationToken);
}
