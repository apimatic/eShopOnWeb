using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Settings bound from the "Twilio" configuration section. Values are supplied via
/// environment variables / user-secrets — never hard-coded.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    [Required]
    public string FromNumber { get; set; } = string.Empty;

    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address only (api.twilio.com).
    /// Does not govern other Twilio hosts (e.g. Lookup).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// How many days after dispatch the delivery follow-up message is scheduled for.
    /// </summary>
    public int FollowUpDelayDays { get; set; } = 3;
}
