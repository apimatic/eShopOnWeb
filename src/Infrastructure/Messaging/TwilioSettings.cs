using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive from user-secrets or
/// environment variables — never from files in this repository.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number (E.164). Reconciliation is scoped to it.</summary>
    [Required]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Required for provider-side scheduled messages.</summary>
    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>Optional override for the messaging API base address only (used verbatim when set).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>How long after dispatch the delivery follow-up is sent by the provider.</summary>
    public int FollowUpDelayDays { get; set; } = 3;
}
