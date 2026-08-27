namespace Microsoft.eShopWeb.ApplicationCore;

public class NotificationSettings
{
    public const string CONFIG_NAME = "Notifications";

    /// <summary>How many days after dispatch the delivery follow-up message goes out.</summary>
    public int FollowUpDelayDays { get; set; } = 3;
}
