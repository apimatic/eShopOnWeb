using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Notification tunables bound from the <c>Notifications:</c> configuration section.
/// </summary>
public class NotificationOptions : INotificationOptions
{
    public const string ConfigSection = "Notifications";

    /// <summary>
    /// How long after dispatch the delivery follow-up is sent. Defaults to 3 days. Bound from
    /// <c>Notifications:DeliveryFollowUpDelay</c> as a TimeSpan (e.g. "3.00:00:00").
    /// </summary>
    public TimeSpan DeliveryFollowUpDelay { get; set; } = TimeSpan.FromDays(3);
}
