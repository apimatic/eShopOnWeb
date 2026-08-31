using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via user-secrets / environment
/// variables and must never be written to source files or logs.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The account's own sending number; reconciliation counts only messages sent from it.</summary>
    [Required]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service used for provider-held scheduled messages.</summary>
    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>Optional override for the messaging API base address only (used verbatim when set).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>How long after dispatch the delivery follow-up is queued for.</summary>
    public int FollowUpDelayDays { get; set; } = 3;
}
