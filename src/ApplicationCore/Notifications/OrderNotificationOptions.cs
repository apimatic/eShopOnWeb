using System;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// Tunables for order notifications. Kept provider-neutral so the messaging integration stays swappable.
/// </summary>
public class OrderNotificationOptions
{
    /// <summary>
    /// How far in the future the "how did the delivery go?" follow-up is scheduled when an order is
    /// dispatched. "A few days later" by default.
    /// </summary>
    public TimeSpan DeliveryFollowUpDelay { get; set; } = TimeSpan.FromDays(3);
}
