using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Tunables for the order-notification flow that are not provider credentials. Bound from the
/// <c>Notifications:</c> configuration section; sensible defaults apply when unset.
/// </summary>
public class NotificationSettings
{
    public const string SectionName = "Notifications";

    /// <summary>
    /// How long after dispatch the "how did the delivery go?" follow-up is scheduled with the
    /// provider. Default is 3 days. Must stay within the provider's 15-minute..35-day window.
    /// </summary>
    public double DeliveryFollowUpDelayHours { get; set; } = 72;

    public TimeSpan DeliveryFollowUpDelay => TimeSpan.FromHours(DeliveryFollowUpDelayHours);
}
