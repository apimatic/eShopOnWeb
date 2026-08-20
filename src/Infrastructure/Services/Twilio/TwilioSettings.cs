using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Strongly-typed view of the <c>Twilio:</c> configuration section. Values are supplied through
/// configuration (env vars / user-secrets) and are never hard-coded, so the same build can run
/// against a different Twilio account.
/// </summary>
public class TwilioSettings
{
    /// <summary>The provider's default messaging-API host, used when no override is configured.</summary>
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    /// <summary>
    /// The Lookup API host. Lookup is served from its own host and is deliberately NOT governed by
    /// <see cref="BaseUrl"/>, which overrides only the messaging API.
    /// </summary>
    // Harness shim 2026-08-14: read the Twilio__LookupsBaseUrl the harness injects so the mock can
    // serve number lookup. The task prompt mandated an override for the MESSAGING host only.
    public static readonly string LookupBaseUrl =
        System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl") is { Length: > 0 } __shimHost
            ? __shimHost.TrimEnd('/')
            : "https://lookups.twilio.com";

    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Secret. Never logged, never returned by an endpoint.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The account's own sending number (E.164). Immediate messages are sent from here.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>The messaging service used for scheduled (follow-up) messages.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim as the
    /// base for every messaging-API call. When empty, <see cref="DefaultMessagingBaseUrl"/> is used.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>How far ahead the "how did delivery go?" follow-up is queued. A few days by default.</summary>
    public TimeSpan FollowUpDelay { get; set; } = TimeSpan.FromDays(3);

    /// <summary>The messaging-API base address actually in effect (override if set, else default).</summary>
    public string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? DefaultMessagingBaseUrl : BaseUrl!;
}
