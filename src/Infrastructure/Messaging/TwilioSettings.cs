namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio configuration, bound from the "Twilio" section. Values are supplied through
/// user-secrets / environment — never hard-coded. The auth token is a secret and is never
/// logged, returned by an endpoint, or written to a source file.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number (E.164).</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID (MG...), required by the provider to schedule a message.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim for
    /// every messaging-API call (send, read, update, list). It does not govern the Lookup API,
    /// which the provider serves from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
