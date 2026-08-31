using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

/// <summary>
/// Settings bound from the "Twilio" configuration section. Secret values (AuthToken) are
/// supplied via user-secrets / environment variables and are never logged or returned.
/// </summary>
public class TwilioOptions
{
    public const string SectionName = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    [Required]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>
    /// Required for scheduled messages; when absent, scheduling is reported as a send failure
    /// rather than failing the underlying operation.
    /// </summary>
    public string? MessagingServiceSid { get; set; }

    /// <summary>
    /// Optional override for the messaging API host only (used verbatim). Lookup and other
    /// Twilio capabilities keep their own default hosts.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>How many days after dispatch the delivery follow-up is scheduled for.</summary>
    public int FollowUpDelayDays { get; set; } = 3;

    /// <summary>Whole-call budget (seconds) for any single provider call.</summary>
    public int CallTimeoutSeconds { get; set; } = 30;
}
