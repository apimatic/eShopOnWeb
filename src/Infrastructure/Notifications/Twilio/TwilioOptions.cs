namespace Microsoft.eShopWeb.Infrastructure.Notifications.Twilio;

/// <summary>
/// Settings for the Twilio messaging integration, bound from the <c>Twilio:</c> configuration
/// section. Values are supplied by configuration / user-secrets and are never hard-coded.
/// </summary>
public class TwilioOptions
{
    public const string SectionName = "Twilio";

    /// <summary>Twilio Account SID — the username of the HTTP Basic credential.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Twilio Auth Token — the password of the HTTP Basic credential. Secret; never logged.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number (E.164). Used as the message sender and the reconciliation filter.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID used to schedule the delayed follow-up message.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the base address of the messaging API only (the API that sends, reads
    /// and reconciles messages). When set it is used verbatim for every messaging-API call. It does
    /// not govern the Lookup API, which is served from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
