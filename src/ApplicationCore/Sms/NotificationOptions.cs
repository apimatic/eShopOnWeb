using System;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>Tunables for the order-notification flow. Bound from the optional "Notifications" config section.</summary>
public class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>
    /// How far after dispatch the "how did delivery go?" follow-up is scheduled with the provider.
    /// The provider allows scheduling between 15 minutes and 35 days out; "a few days" sits comfortably inside that.
    /// </summary>
    public TimeSpan FollowUpDelay { get; set; } = TimeSpan.FromDays(3);
}
