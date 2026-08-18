using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Twilio messaging configuration, bound from the "Twilio:" configuration section. Values are supplied
/// through configuration (user-secrets / environment) — never hard-coded — so the same build can run
/// against a different Twilio account. The auth token is a secret: it is never logged or returned.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number, in E.164. Reconciliation counts only this number's traffic.</summary>
    [Required]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID. Required to schedule the delivery follow-up (scheduling is Messaging-Services-only).</summary>
    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim for every
    /// messaging-API call. It does not govern the separate phone-number lookup host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
