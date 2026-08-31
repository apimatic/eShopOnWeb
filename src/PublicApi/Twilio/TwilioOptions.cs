using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Twilio;

/// <summary>
/// Settings bound from the "Twilio" configuration section. Values arrive from
/// environment variables / user-secrets; none are hard-coded.
/// </summary>
public class TwilioOptions
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
    /// Optional override for the messaging API base address only. Lookup and other
    /// Twilio capabilities stay on their own hosts.
    /// </summary>
    public string? BaseUrl { get; set; }
}
