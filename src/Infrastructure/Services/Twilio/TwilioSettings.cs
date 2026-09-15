namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Strongly-typed Twilio configuration, bound from the <c>Twilio:</c> section. Values are supplied by the
/// environment / user-secrets and are never hard-coded. The <see cref="AuthToken"/> is a secret and is never
/// logged or returned by an endpoint.
/// </summary>
public class TwilioSettings
{
    public const string ConfigSection = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The account's own sending number. Immediate sends go out from here; reconciliation filters on it.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID — required for scheduling (scheduling is "for Messaging Services only").</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>Optional override for the messaging-API base address. When set, used verbatim for every messaging call.</summary>
    public string? BaseUrl { get; set; }
}
