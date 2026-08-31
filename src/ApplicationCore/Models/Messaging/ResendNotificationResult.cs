using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

/// <summary>
/// The outcome of a resend request. <paramref name="IdempotentReplay"/> is true when the
/// idempotency key was already used and the previously produced notification is returned
/// without sending a second message.
/// </summary>
public record ResendNotificationResult(OrderNotification Notification, bool IdempotentReplay);
