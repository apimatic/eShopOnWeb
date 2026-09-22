namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Strongly-typed Twilio configuration, bound from the <c>Twilio:</c> section. Values come from
/// configuration (user-secrets / environment) and are never hard-coded. The four credentials are
/// validated non-blank at startup (see <see cref="MessagingServiceCollectionExtensions"/>);
/// <see cref="BaseUrl"/> is an optional override for the messaging API only.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID (basic-auth username). From <c>TWILIO_ACCOUNT_SID</c>.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (basic-auth password). Secret — never logged or returned. From <c>TWILIO_AUTH_TOKEN</c>.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The app's own sending number. From <c>TWILIO_FROM_NUMBER</c>.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID, required to schedule the delivery follow-up. From <c>TWILIO_MESSAGING_SERVICE_SID</c>.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the base address of the <b>messaging</b> API (message create/read/list/update).
    /// When set, it is used verbatim for those calls; other Twilio hosts (e.g. Lookup) are unaffected.
    /// </summary>
    public string? BaseUrl { get; set; }
}
