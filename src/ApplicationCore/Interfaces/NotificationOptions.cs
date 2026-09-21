namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application-level policy for order notifications, kept free of any provider detail so ApplicationCore
/// need not know about the SMS provider.
/// </summary>
public class NotificationOptions
{
    /// <summary>How many days after dispatch the "how did delivery go?" follow-up is scheduled for.</summary>
    public int FollowUpDelayDays { get; set; } = 3;
}
