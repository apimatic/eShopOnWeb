using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Strongly-typed view of the <c>Twilio:</c> configuration section. Values are supplied through
/// configuration / user-secrets and are never hard-coded. The <see cref="AuthToken"/> is a secret:
/// it is never logged, never returned by an endpoint, and never written into a source file.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>Optional override for the messaging API base URL. When empty, the provider default is used.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>
/// Application-level notification knobs, bound from the optional <c>Notifications:</c> section.
/// </summary>
public class NotificationSettings : INotificationSettings
{
    public const string SectionName = "Notifications";

    /// <summary>Days after dispatch to queue the delivery follow-up. Defaults to 3.</summary>
    public int FollowUpDelayDays { get; set; } = 3;
}
