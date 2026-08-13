namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Business policy for order notifications (provider-agnostic). Bound from the <c>Twilio:</c>
/// configuration section so it can be tuned per deployment without code changes.
/// </summary>
public class NotificationSettings
{
    /// <summary>
    /// How many days after dispatch the "how did delivery go?" follow-up is queued with the provider.
    /// Kept within the provider's scheduling window (15 minutes to 7 days). Default 3.
    /// </summary>
    public int FeedbackDelayDays { get; set; } = 3;
}
