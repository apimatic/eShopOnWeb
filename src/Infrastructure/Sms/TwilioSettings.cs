using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Bound from the <c>Twilio:</c> configuration section. Values are supplied by configuration
/// (user-secrets / environment) and never hard-coded — the same build runs against a different
/// Twilio account. Required members are validated at startup so a missing credential refuses to
/// boot rather than surfacing as a 401 on the first message.
/// </summary>
public class TwilioSettings
{
    public const string ConfigurationSectionName = "Twilio";

    [Required(AllowEmptyStrings = false)]
    public string AccountSid { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string AuthToken { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string FromNumber { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the MESSAGING API base URL only. When set it is used verbatim for every
    /// messaging-API call (send/read/update/list). It does NOT govern the phone-number lookup host,
    /// which keeps its own default. Left null, the provider's default messaging host is used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
