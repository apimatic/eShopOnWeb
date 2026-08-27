using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Binds the "Twilio:" configuration section. Values arrive from user-secrets or
/// environment-specific secret stores — never from a file committed to the repository.
/// </summary>
public class TwilioOptions
{
    public const string SectionName = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number; reconciliation reports on this number's messages.</summary>
    [Required]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Required for provider-scheduled messages (scheduling is messaging-service only).</summary>
    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>Optional override for the messaging API base address only; used verbatim when set.</summary>
    public string? BaseUrl { get; set; }
}
