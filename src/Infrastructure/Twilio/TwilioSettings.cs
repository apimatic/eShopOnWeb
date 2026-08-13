using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio messaging configuration, bound from the <c>Twilio:</c> section. None of these values are
/// hard-coded — the same build runs against a different account by changing configuration only.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Secret. Never logged, never returned by an endpoint, never written into source.</summary>
    [Required]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number (used for immediate sends and reconciliation).</summary>
    [Required]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service used for scheduled (follow-up) sends.</summary>
    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base URL. When set, it is used verbatim as the base
    /// address for every messaging-API call. The lookup API keeps its own host regardless.
    /// </summary>
    public string? BaseUrl { get; set; }
}
