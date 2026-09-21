namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Tunables for the order-notifications feature. Bound from the <c>Twilio</c> configuration section.
/// </summary>
public class NotificationOptions
{
    /// <summary>How many days after dispatch the delivery-survey follow-up is queued for.</summary>
    public int FollowUpDelayDays { get; set; } = 3;
}
