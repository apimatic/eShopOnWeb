namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Settings bound from the <c>Twilio:</c> configuration section. Values are supplied out-of-repo
/// (environment / user-secrets); none is hard-coded. The auth token is a secret — it is never logged,
/// never returned by an endpoint, and never written into a source file.
/// </summary>
public class TwilioSettings
{
    public const string ConfigSection = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the MESSAGING API base URL only. When set, it is used verbatim as the base
    /// address for every messaging-API call (send, read, reconcile). It does NOT govern other Twilio hosts
    /// such as Lookup. Left unset, the provider's default messaging host is used.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Total budget (seconds) bounding a whole provider call, retries included.</summary>
    public int CallBudgetSeconds { get; set; } = 30;
}
