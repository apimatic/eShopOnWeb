namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// Strongly-typed binding of the <c>Twilio:</c> configuration section. Values are supplied
/// through configuration / user-secrets and must never be hard-coded, so the same build can run
/// against a different Twilio account. See <see cref="CONFIG_NAME"/>.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    /// <summary>Twilio Account SID (starts with <c>AC</c>). Basic-auth username.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Twilio Auth Token. Basic-auth password. Secret: never logged or returned.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The account's own sending number in E.164. Used as <c>From</c> and as the
    /// reconciliation filter.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID (starts with <c>MG</c>). Required for scheduled messages.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the <b>messaging</b> API base address. When set, it is used verbatim
    /// as the base for every messaging-API call (send, fetch, list, update, delete). It does NOT
    /// govern other Twilio hosts such as Lookup. When empty the provider default is used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
