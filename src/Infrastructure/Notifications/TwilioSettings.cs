using System;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Twilio account/configuration bound from the <c>Twilio:</c> configuration section. Values are
/// supplied by configuration (user-secrets / environment) and are never hard-coded. The
/// <see cref="AuthToken"/> is a secret: it is never logged, returned, or written to a source file.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Twilio Account SID (from <c>TWILIO_ACCOUNT_SID</c>).</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Twilio Auth Token (from <c>TWILIO_AUTH_TOKEN</c>). Secret — never logged or returned.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own configured sending number in E.164 (from <c>TWILIO_FROM_NUMBER</c>).</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID (from <c>TWILIO_MESSAGING_SERVICE_SID</c>), required to schedule messages.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the base address of the <b>messaging</b> API only (send/read/reconcile).
    /// When set it is used verbatim for every messaging-API call; when empty the provider default
    /// (<c>https://api.twilio.com</c>) is used. It does not govern other Twilio hosts (e.g. Lookups).
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountSid) &&
        !string.IsNullOrWhiteSpace(AuthToken) &&
        !string.IsNullOrWhiteSpace(FromNumber);
}
