using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Bound from the "Twilio" configuration section. Secrets arrive via user-secrets or
/// environment variables; no values live in source control.
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

    /// <summary>Optional override for the messaging API base address only. Lookup API calls
    /// are served from a different host and are not governed by this setting.</summary>
    public string? BaseUrl { get; set; }
}
