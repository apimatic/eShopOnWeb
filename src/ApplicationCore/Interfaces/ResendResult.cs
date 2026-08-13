using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Outcome of a resend. <see cref="WasDuplicate"/> is true when the idempotency key had already
/// been used, in which case <see cref="Notification"/> is the message the first request produced
/// and no second message was sent.
/// </summary>
public record ResendResult(Notification Notification, bool WasDuplicate);
