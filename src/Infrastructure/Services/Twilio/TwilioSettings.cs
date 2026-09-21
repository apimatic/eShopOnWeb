using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Twilio configuration, bound from the <c>Twilio:</c> section. Required values are validated at
/// startup (fail-fast) so a missing or blank credential stops the host rather than surfacing as a
/// 401 on the first message. Values are never hard-coded and never logged.
/// </summary>
public class TwilioSettings
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
    /// Optional override for the messaging API base address (the API this integration sends, reads
    /// and reconciles messages through). When set, used verbatim for every messaging-API call.
    /// Does not govern the Lookup API, which is served from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
