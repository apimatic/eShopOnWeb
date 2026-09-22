using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio configuration, bound from the <c>Twilio:</c> configuration section. Values come from
/// user-secrets / environment at deploy time — never from a file in the repository. Required members are
/// validated at startup so a missing or blank credential stops the host from booting rather than surfacing
/// as a 401 on the first message.
/// </summary>
public sealed class TwilioSettings
{
    public const string SectionName = "Twilio";

    [Required(AllowEmptyStrings = false)]
    public string AccountSid { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The account's own sending number (E.164). Reconciliation asks the provider only for this sender.</summary>
    [Required(AllowEmptyStrings = false)]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID — required by the provider for scheduled (send-later) messages.</summary>
    [Required(AllowEmptyStrings = false)]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base URL (the host the app sends, reads and reconciles
    /// messages through). When set, used verbatim for every messaging-API call. Does not govern other
    /// Twilio hosts (e.g. Lookups).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>How far ahead the delivery-feedback follow-up is scheduled. A few days by default.</summary>
    public int FeedbackFollowUpDelayDays { get; set; } = 3;
}
