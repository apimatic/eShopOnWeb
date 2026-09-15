namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Strongly-typed Twilio settings, bound from the <c>Twilio:</c> configuration section. Values are
/// supplied through configuration / user-secrets and are never hard-coded.
/// </summary>
public class TwilioOptions
{
    public const string SectionName = "Twilio";

    /// <summary>The provider's default host for the messaging API (Account/Messages resource).</summary>
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    /// <summary>
    /// The host for the phone-number lookup API. Twilio serves lookups from its own host; the
    /// <see cref="BaseUrl"/> override governs the messaging API only and does not apply here.
    /// </summary>
    public const string LookupsBaseUrl = "https://lookups.twilio.com";

    /// <summary>Account SID (<c>Twilio:AccountSid</c>). Also the Basic-auth username.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (<c>Twilio:AuthToken</c>). Secret — never logged, returned, or written to a file.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number in E.164 (<c>Twilio:FromNumber</c>).</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID (<c>Twilio:MessagingServiceSid</c>), required to schedule messages.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override (<c>Twilio:BaseUrl</c>) for the messaging API base address. When set it is used
    /// verbatim for every messaging-API call instead of <see cref="DefaultMessagingBaseUrl"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The effective base address for messaging-API calls.</summary>
    public string EffectiveMessagingBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? DefaultMessagingBaseUrl : BaseUrl!;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AccountSid) && !string.IsNullOrWhiteSpace(AuthToken);
}
