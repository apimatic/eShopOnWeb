using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// The outcome of a resend request. IdempotentReplay is true when the idempotency key
/// was already processed — no second message was sent and the original resend record
/// is returned.
/// </summary>
public record ResendResult(OrderNotification Notification, bool IdempotentReplay);
