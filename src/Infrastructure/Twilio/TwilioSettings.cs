using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via user-secrets or
/// environment variables — never from a file in this repository.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own sending number; reconciliation reports on its traffic only.</summary>
    [Required]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Required for provider-scheduled messages (scheduled sends go through a messaging service).</summary>
    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>Optional override for the messaging API host only; used verbatim when set.</summary>
    public string? BaseUrl { get; set; }
}
