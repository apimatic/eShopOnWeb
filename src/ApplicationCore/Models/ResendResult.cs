using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// Outcome of an operator re-send request.
/// </summary>
/// <param name="Notification">The notification produced by the re-send (or the one a replayed key already produced).</param>
/// <param name="WasReplayed">True when the idempotency key was already used: no new message was sent.</param>
public record ResendResult(OrderNotification Notification, bool WasReplayed);
