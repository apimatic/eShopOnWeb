namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Strongly-typed Twilio configuration, bound from the "Twilio" configuration section. Values are
/// supplied at runtime (environment / user-secrets) and are never hard-coded in the repository.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Provider default host for the Account (v2010) messaging API.</summary>
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    /// <summary>The Lookups API is served from its own host and is not governed by <see cref="BaseUrl"/>.</summary>
    public const string LookupsBaseUrl = "https://lookups.twilio.com";

    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Secret. Never logged, never returned by an endpoint, never written to a source file.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own configured sending number (E.164).</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID used for scheduled (future-dated) messages.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging (v2010) API base URL. When set, it is used verbatim as the
    /// base address for every messaging-API call. When empty, <see cref="DefaultMessagingBaseUrl"/> is used.
    /// Does not affect the Lookups API.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The effective messaging-API base URL (override if set, otherwise the provider default), without a trailing slash.</summary>
    public string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? DefaultMessagingBaseUrl : BaseUrl.TrimEnd('/');
}
