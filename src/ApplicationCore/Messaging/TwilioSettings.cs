using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// Settings bound from the "Twilio" configuration section. Values arrive via
/// user-secrets / environment variables; none are committed to the repository.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    [Required]
    public string FromNumber { get; set; } = string.Empty;

    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API host only. When set, it is used verbatim
    /// as the base address for every messaging-API call. It does not govern other Twilio
    /// capabilities (e.g. Lookup), which are served from their own hosts.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>How long after dispatch the delivery follow-up message is sent.</summary>
    public int FollowUpDelayInDays { get; set; } = 3;
}
