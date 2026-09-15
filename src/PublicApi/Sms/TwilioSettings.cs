using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Sms;

/// <summary>
/// Strongly-typed Twilio messaging configuration, bound from the <c>Twilio:</c> configuration
/// section. None of these values is hard-coded — they come from configuration / user-secrets so
/// the same build runs against a different Twilio account.
///
/// <see cref="AuthToken"/> is a secret: it is never logged, never returned by an endpoint, and
/// never written into a source file.
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
    /// Optional override for the <em>messaging</em> API base address. When set, it is used verbatim
    /// for every messaging-API call (send / read / reconcile). It does not govern the separate
    /// Lookup host. When unset, the provider default is used.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>How many days after dispatch the "how did delivery go?" follow-up is scheduled to send.</summary>
    public double FollowUpDelayDays { get; set; } = 3;

    /// <summary>Whole-call budget applied to each provider interaction.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;
}
