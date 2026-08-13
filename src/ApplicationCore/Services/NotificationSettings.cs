namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Application-level notification tuning, bound from the <c>Notifications</c> configuration section.
/// </summary>
public class NotificationSettings
{
    /// <summary>How many days after dispatch the "how did delivery go" follow-up is queued with the provider.</summary>
    public int FollowUpDelayDays { get; set; } = 3;
}
