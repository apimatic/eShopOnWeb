namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Behavioural knobs for the notification flow that live in the application layer (independent of any
/// particular provider). Bound from configuration at composition time.
/// </summary>
public class NotificationSettings
{
    /// <summary>How many days after dispatch the delivery follow-up is scheduled to go out.</summary>
    public int FollowUpDelayDays { get; set; } = 3;
}
