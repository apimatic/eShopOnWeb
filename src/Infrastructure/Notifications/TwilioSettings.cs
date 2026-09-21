using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Strongly-typed Twilio configuration bound from the <c>Twilio:</c> section. Every credential is
/// <see cref="RequiredAttribute"/> so a missing or blank value stops the host at startup rather than
/// surfacing as a 401 on the first message. Values are supplied by configuration (user-secrets / env /
/// secret store) — never hard-coded.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID (also the Basic-auth username). Bound from <c>Twilio:AccountSid</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (the Basic-auth password). Bound from <c>Twilio:AuthToken</c>. Never logged.</summary>
    [Required(AllowEmptyStrings = false)]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number. Bound from <c>Twilio:FromNumber</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging service used for scheduled follow-ups. Bound from <c>Twilio:MessagingServiceSid</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the <b>messaging</b> API base URL (server group <c>Default</c>,
    /// api.twilio.com). Bound from <c>Twilio:BaseUrl</c>. When set it is used verbatim for every
    /// messaging-API call; it does not govern the Lookups host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>How many days after dispatch the "how did delivery go?" follow-up is scheduled. Default 3.</summary>
    public int FollowUpDelayDays { get; set; } = 3;
}
