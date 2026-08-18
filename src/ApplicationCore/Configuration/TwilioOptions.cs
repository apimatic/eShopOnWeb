namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Strongly-typed view of the <c>Twilio:</c> configuration section. Values are bound from
/// configuration (env vars / user-secrets) and are never hard-coded, so the same build runs
/// against any Twilio account.
/// </summary>
public class TwilioOptions
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID, used as the Basic-auth username and in messaging path templates.</summary>
    public string? AccountSid { get; set; }

    /// <summary>Auth token (secret). Used only as the Basic-auth password; never logged or returned.</summary>
    public string? AuthToken { get; set; }

    /// <summary>This application's own configured sending number. Reconciliation counts only this number's traffic.</summary>
    public string? FromNumber { get; set; }

    /// <summary>Messaging Service SID, required by the provider to schedule a message for later.</summary>
    public string? MessagingServiceSid { get; set; }

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim as the
    /// base address for every messaging-API call. It does not govern the Lookups API.
    /// </summary>
    public string? BaseUrl { get; set; }
}
