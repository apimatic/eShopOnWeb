namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from the <c>Twilio:</c> configuration section. Values come from environment / user-secrets, never source.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the Messaging API host (send/read/reconcile). Lookups always use lookups.twilio.com.
    /// When set, used verbatim as the base address for every messaging-API call.
    /// </summary>
    public string? BaseUrl { get; set; }
}
