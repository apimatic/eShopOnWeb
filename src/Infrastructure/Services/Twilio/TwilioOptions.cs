using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via user-secrets
/// or environment variables — never from a file in this repository.
/// </summary>
public class TwilioOptions
{
    public const string SectionName = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number; also the reconciliation sender filter.</summary>
    [Required]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Required for provider-side scheduled messages.</summary>
    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address only. When set it is
    /// used verbatim for every messaging-API call; other capabilities (e.g. phone
    /// number lookup) are served from their own hosts and are not governed by this.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>How long after dispatch the delivery follow-up should go out.</summary>
    public double FollowUpDelayDays { get; set; } = 3;
}
