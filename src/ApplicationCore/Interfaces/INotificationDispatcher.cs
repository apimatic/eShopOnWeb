using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Turns an order event into the SMS messages that go out for it and keeps the local
/// notification records in step with the provider. Every method is best-effort: a message
/// that cannot be sent is recorded as failed but never bubbles up to fail the caller's
/// underlying operation.
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>
    /// Sends an immediate message for <paramref name="kind"/> to each number the buyer has
    /// on file (a buyer with no number is simply not messaged), persisting a notification
    /// per message with the provider's identifier and outcome.
    /// </summary>
    Task SendOrderEventAsync(Order order, NotificationKind kind, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a "how did delivery go?" follow-up with the provider, to be sent a few days
    /// later, for each number the buyer has on file.
    /// </summary>
    Task ScheduleDeliveryFollowUpAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls off any follow-up for the order that has not yet gone out, so a cancelled
    /// order never triggers a "how did delivery go?" message.
    /// </summary>
    Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Brings the given notifications' delivery outcomes up to date from the provider
    /// (for any whose stored status is not yet settled), persisting any changes.
    /// </summary>
    Task RefreshStatusesAsync(IReadOnlyList<Notification> notifications, CancellationToken cancellationToken = default);
}
