using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Settings bound from the <c>Twilio:</c> configuration section. Values are never hard-coded — they come
/// from configuration (user-secrets / environment) so the same build runs against a different account.
/// The auth token is a secret and is never logged or returned by any endpoint.
/// </summary>
public class TwilioOptions
{
    public const string SectionName = "Twilio";

    [Required(AllowEmptyStrings = false)]
    public string AccountSid { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string AuthToken { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string FromNumber { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the <b>messaging</b> API base address (the host messages are sent, read and
    /// reconciled through). When set, it is used verbatim as the base URL for every messaging-API call.
    /// It does not govern other Twilio hosts (e.g. Lookups). Left null → the provider default.
    /// </summary>
    public string? BaseUrl { get; set; }
}
