using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Settings bound from the <c>Twilio:</c> configuration section. The four credential/identifier parts are
/// <see cref="RequiredAttribute"/>-guarded and validated at host start (each part individually — a blank part
/// is not a missing one), so the host refuses to boot rather than discovering the fault as a 401 on the first
/// call. Values come from user-secrets / environment; none is ever written into the repository.
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

    /// <summary>Optional override for the messaging API base address. When set, used verbatim for every
    /// messaging-API call (send/read/update/reconcile). The Lookup API is served from another host and is
    /// unaffected by this setting.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>How many days after dispatch the "how did delivery go?" follow-up is scheduled with the provider.</summary>
    [Range(1, 7)]
    public int FollowUpDelayDays { get; set; } = 3;
}
