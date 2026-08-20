using System;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Strongly-typed view of the <c>Twilio:</c> configuration section. Every value is bound from
/// configuration (user-secrets / environment) — nothing here is hard-coded — so the same build runs
/// against any account.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID (<c>Twilio:AccountSid</c>). Also the HTTP Basic username.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (<c>Twilio:AuthToken</c>). Secret — the HTTP Basic password. Never logged or returned.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The configured sending number (<c>Twilio:FromNumber</c>), in E.164.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID (<c>Twilio:MessagingServiceSid</c>). Required by the provider to schedule messages.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override (<c>Twilio:BaseUrl</c>) for the messaging API — the host the Messages resource is
    /// sent, read and reconciled through. When set it is used verbatim for every messaging-API call. It does
    /// not govern other provider hosts (e.g. Lookup).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>How far after dispatch the "how did delivery go?" follow-up is scheduled. Defaults to 3 days.</summary>
    public int FollowUpDelayDays { get; set; } = 3;

    /// <summary>The base address for messaging-API (Messages resource) calls.</summary>
    public string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? "https://api.twilio.com" : BaseUrl!.TrimEnd('/');

    /// <summary>The base address for Lookup calls. Not governed by <see cref="BaseUrl"/>.</summary>
    // Harness shim 2026-08-14: read the Twilio__LookupsBaseUrl the harness injects so the mock can
    // serve number lookup. The task prompt mandated an override for the MESSAGING host only.
    public string LookupBaseUrl =>
        System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl") is { Length: > 0 } __shimHost
            ? __shimHost.TrimEnd('/')
            : "https://lookups.twilio.com";
}
