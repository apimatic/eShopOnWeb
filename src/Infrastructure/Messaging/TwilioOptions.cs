namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Strongly-typed binding of the <c>Twilio:</c> configuration section. Values come from configuration
/// (user-secrets / environment), never from source. See <see cref="TwilioServiceCollectionExtensions"/>
/// for the fail-fast validation that refuses to start when a required value is missing or blank.
/// </summary>
public class TwilioOptions
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID — the basic-auth username and the account path segment on every messaging call.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token — the basic-auth password. A secret: never logged, returned, or written to source.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The sending number for immediate messages, and the number the reconciliation report filters by.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID — required by the provider to schedule the follow-up message.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base URL (the API messages are sent, read and reconciled
    /// through). When set, it is used verbatim for every messaging call. It does NOT govern the number-lookup
    /// API, which the provider serves from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
