using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single choke point for handing a message to the provider and recording it. Handing a message
/// off never throws to the caller: a message that cannot be sent is recorded as such, so the order
/// operation that triggered it still succeeds.
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>
    /// Sends a freshly-built notification, records the provider's response on it, and persists it.
    /// Returns the persisted notification (with its id and provider state).
    /// </summary>
    Task<SmsNotification> SendNewAsync(SmsNotification notification, CancellationToken cancellationToken = default);

    /// <summary>Refreshes one notification's delivery outcome from the provider, if it is not terminal.</summary>
    Task RefreshAsync(SmsNotification notification, CancellationToken cancellationToken = default);

    /// <summary>Refreshes many notifications' delivery outcomes from the provider.</summary>
    Task RefreshManyAsync(IEnumerable<SmsNotification> notifications, CancellationToken cancellationToken = default);
}
