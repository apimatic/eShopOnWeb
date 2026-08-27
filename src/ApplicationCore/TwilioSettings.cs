using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings bound from the "Twilio" configuration section. Secrets (AuthToken) are supplied
/// via user-secrets/environment variables and are never logged or returned by an endpoint.
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
    /// Optional override for the messaging API base address only. When set, it is used
    /// verbatim as the base address for every messaging-API call. Other Twilio
    /// capabilities (e.g. phone-number lookup) keep their own default hosts.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>How many days after dispatch the delivery follow-up message is sent.</summary>
    public int FollowUpDelayInDays { get; set; } = 3;
}
