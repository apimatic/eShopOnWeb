using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via environment
/// variables / user-secrets — never from files committed to the repository.
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
    /// Optional override for the messaging API base address. When set, it is used
    /// verbatim for every messaging-API call instead of the provider default.
    /// </summary>
    public string? BaseUrl { get; set; }
}
