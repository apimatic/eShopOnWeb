using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Strongly-typed binding of the <c>Twilio:</c> configuration section. Values are supplied by
/// configuration (user-secrets / environment) — never hard-coded. The auth token is a secret and is
/// never logged, returned by an endpoint, or written into a source file.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own sending number; also the number reconciliation is scoped to.</summary>
    [Required]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service used for provider-side scheduling of the delivery follow-up.</summary>
    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>Optional override for the messaging-API base address. Governs only the messaging host, not lookup.</summary>
    public string? BaseUrl { get; set; }
}
