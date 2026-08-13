namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Bound from the <c>Twilio:</c> configuration section. Values are supplied per environment
/// (env vars → user-secrets) and are never hard-coded. The <see cref="AuthToken"/> is a secret.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number. Reconciliation is scoped to this number.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID — required by the provider to schedule messages for a future time.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address only. When set, it is used verbatim for
    /// every messaging-API call (send / read / update / list). Other Twilio hosts (e.g. Lookup) are
    /// not governed by it.
    /// </summary>
    public string? BaseUrl { get; set; }
}
