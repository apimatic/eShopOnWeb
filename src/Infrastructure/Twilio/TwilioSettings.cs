using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Options bound from the <c>Twilio:</c> configuration section. The four credential/identity values are
/// required and validated at startup (see <see cref="TwilioServiceCollectionExtensions"/>); the host
/// refuses to boot if any is missing or blank, rather than discovering it as a 401 on the first call.
/// None of these values is ever hard-coded — they come from configuration (user-secrets / environment).
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    [Required]
    public string FromNumber { get; set; } = string.Empty;

    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the base address of the <b>messaging</b> API (the one this integration sends,
    /// reads and reconciles messages through). When set, it is used verbatim for every messaging-API call.
    /// It does not govern the lookup API, which the provider serves from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
