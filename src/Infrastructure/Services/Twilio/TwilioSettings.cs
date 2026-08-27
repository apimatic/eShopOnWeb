namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via
/// user-secrets / environment and must never be written to source files.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is
    /// used verbatim for every messaging-API call. It does not govern other
    /// Twilio capabilities (e.g. Lookup), which keep their own hosts.
    /// </summary>
    public string? BaseUrl { get; set; }
}
