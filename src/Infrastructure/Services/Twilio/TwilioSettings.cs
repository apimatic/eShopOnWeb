namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Settings bound from the "Twilio" configuration section. Values arrive via
/// user-secrets / environment variables and must never be committed to the repo.
/// </summary>
public class TwilioSettings
{
    public const string ConfigName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Secret. Never logged, never returned by an endpoint.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own sending number; reconciliation reports on its traffic only.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Required for scheduling messages with the provider.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set it is used
    /// verbatim for every messaging-API call. It does not govern other Twilio
    /// capabilities (e.g. Lookup), which keep their own hosts.
    /// </summary>
    public string? BaseUrl { get; set; }
}
