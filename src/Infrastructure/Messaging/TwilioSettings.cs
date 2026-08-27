using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via user-secrets or
/// environment variables — never from files committed to the repository.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The shop's own sending number; reconciliation reports on this number's messages.</summary>
    [Required]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Required for provider-queued (scheduled) messages, which cannot be sent from a raw number.</summary>
    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>Optional override for the messaging (2010) API host only; used verbatim when set.</summary>
    public string? BaseUrl { get; set; }
}
