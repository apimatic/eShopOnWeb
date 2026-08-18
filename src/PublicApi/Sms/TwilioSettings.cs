using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Sms;

/// <summary>
/// Strongly-typed Twilio configuration, bound from the <c>Twilio:</c> section. Values are never
/// hard-coded — they come from configuration (user-secrets / environment) so the same build can run
/// against a different account. Validated at startup so a missing credential refuses to boot rather
/// than surfacing as a provider 401 on the first message.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    [Required(AllowEmptyStrings = false)]
    public string AccountSid { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's sending number — used for immediate sends and for reconciliation.</summary>
    [Required(AllowEmptyStrings = false)]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID — required by the provider to schedule future (follow-up) sends.</summary>
    [Required(AllowEmptyStrings = false)]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base URL. When set, it is used verbatim for every
    /// messaging-API call; it does not govern the separate lookup host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
