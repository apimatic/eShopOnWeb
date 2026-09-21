using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Twilio settings, bound from the <c>Twilio:</c> configuration section. Values come from
/// .NET user-secrets / environment configuration and are never written into the repository.
/// Each credential is <see cref="RequiredAttribute"/> so a missing OR blank value stops the host
/// from starting (RequiredAttribute rejects null, empty, and whitespace) — rather than surfacing
/// as a 401 on the first call.
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

    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base URL (Twilio server group <c>Default</c>,
    /// default <c>https://api.twilio.com</c>). Governs only the messaging calls this integration
    /// sends, reads and reconciles through — not lookups, which use a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
