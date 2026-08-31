using System;

namespace Microsoft.eShopWeb.ApplicationCore;

public class NotificationSettings
{
    /// <summary>
    /// How long after dispatch the delivery follow-up message should go out.
    /// The provider enforces a minimum of 15 minutes and a maximum of 7 days.
    /// </summary>
    public TimeSpan FollowUpDelay { get; set; } = TimeSpan.FromDays(3);
}
