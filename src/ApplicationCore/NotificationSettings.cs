using System;

namespace Microsoft.eShopWeb.ApplicationCore;

public class NotificationSettings
{
    public const string CONFIG_NAME = "Notifications";

    /// <summary>How long after dispatch the delivery follow-up message should go out.</summary>
    public TimeSpan FollowUpDelay { get; set; } = TimeSpan.FromDays(3);
}
