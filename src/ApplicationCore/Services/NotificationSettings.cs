namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationSettings
{
    public const string SectionName = "Notifications";

    /// <summary>How many days after dispatch the delivery follow-up message goes out.</summary>
    public int FollowUpDelayDays { get; set; } = 3;
}
