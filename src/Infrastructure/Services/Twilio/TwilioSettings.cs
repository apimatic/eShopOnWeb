namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Bound from the <c>Twilio:</c> configuration section. Values are supplied via environment /
/// user-secrets and are never hard-coded, so the same build runs against any Twilio account.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Twilio default host for the messaging (api.twilio.com) API.</summary>
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    /// <summary>Lookup is served from a different host and is not governed by <see cref="BaseUrl"/>.</summary>
    public const string LookupBaseUrl = "https://lookups.twilio.com";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own sending number; reconciliation asks the provider for its messages.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Required for scheduled messages (Twilio scheduling only works via a Messaging Service).</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim for every
    /// messaging-API call instead of <see cref="DefaultMessagingBaseUrl"/>. Does not affect Lookup.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string MessagingBaseUrl => string.IsNullOrWhiteSpace(BaseUrl) ? DefaultMessagingBaseUrl : BaseUrl!;
}
