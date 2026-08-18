using System;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// Business configuration for order SMS notifications, bound from the "Notifications" section.
/// Twilio credentials live separately under "Twilio:" and never appear here.
/// </summary>
public class SmsNotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>
    /// How long after dispatch the "how did delivery go?" follow-up is scheduled with the provider.
    /// The provider accepts a send time between roughly 15 minutes and 7 days out; the default of
    /// three days sits comfortably inside that window.
    /// </summary>
    public TimeSpan FollowUpDelay { get; set; } = TimeSpan.FromDays(3);
}
