using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS messages that go out as an order moves: composing them, sending or scheduling
/// them with the provider, and recording what happened. Every method here is best-effort — a message
/// that cannot be sent is recorded as such and never surfaced to the caller as a failure.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed (one message per number on file).</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order is on its way and queues a "how did delivery go" follow-up with the
    /// provider for a few days later.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order was cancelled and calls off any still-scheduled follow-up for it.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Brings one notification's delivery outcome up to date from the provider when it is not yet
    /// terminal. Best-effort: a provider hiccup leaves the last known state in place.
    /// </summary>
    Task RefreshDeliveryStateAsync(Notification notification, CancellationToken cancellationToken = default);
}
