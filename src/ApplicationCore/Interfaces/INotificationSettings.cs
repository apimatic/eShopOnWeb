namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application-level knobs for order notifications, kept free of any provider specifics so the
/// application core has no dependency on the messaging vendor.
/// </summary>
public interface INotificationSettings
{
    /// <summary>How far ahead of dispatch the "how did delivery go?" follow-up is queued.</summary>
    int FollowUpDelayDays { get; }
}
