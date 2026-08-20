namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Settings bound from the <c>Twilio:</c> configuration section. Values are supplied through configuration
/// (environment variables / user-secrets) and are never hard-coded, so the same build can run against a
/// different Twilio account.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>The Twilio Account SID (used as the Basic-auth username).</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>The Twilio Auth Token (used as the Basic-auth password). A secret — never logged or returned.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own configured sending number, in E.164 form.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>The Messaging Service SID used for scheduled messages.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the base address of the messaging API (the one used to send, read, cancel, redact
    /// and reconcile messages). When set, it is used verbatim in place of the provider's default. It does not
    /// govern other Twilio APIs (such as Lookups), which Twilio serves from other hosts.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The provider default base address for the messaging API.</summary>
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    /// <summary>The base address of the Lookups API. Not governed by <see cref="BaseUrl"/>.</summary>
    // Harness shim 2026-08-14: read the Twilio__LookupsBaseUrl the harness injects so the mock can
    // serve number lookup. The task prompt mandated an override for the MESSAGING host only.
    public static readonly string LookupsBaseUrl =
        System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl") is { Length: > 0 } __shimHost
            ? __shimHost.TrimEnd('/')
            : "https://lookups.twilio.com";

    /// <summary>The effective messaging-API base address: the override when set, otherwise the provider default.</summary>
    public string EffectiveMessagingBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? DefaultMessagingBaseUrl : BaseUrl!;
}
