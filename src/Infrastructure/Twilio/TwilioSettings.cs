using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Binds the "Twilio" configuration section. Values arrive from user-secrets / environment
/// variables — never from files in the repository.
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
    /// Optional override for the messaging API host only. When set, it is used verbatim as the
    /// base address for every messaging-API call; other Twilio capabilities (e.g. Lookups) are
    /// served from their own hosts and are not governed by this setting.
    /// </summary>
    public string? BaseUrl { get; set; }
}
