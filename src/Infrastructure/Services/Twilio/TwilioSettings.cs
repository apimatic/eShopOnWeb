namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Provider credentials and configuration, bound from the "Twilio:" section. Values are supplied by
/// the environment / user-secrets and never hard-coded, so the same build runs against any account.
/// The <see cref="AuthToken"/> is a secret: it is never logged or returned by an endpoint.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own sending number; reconciliation counts only its traffic.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service used to schedule the delivery follow-up.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set it is used verbatim for every
    /// messaging-API call; when empty the provider's default messaging host is used. It does not
    /// govern other hosts (for example the lookup capability).
    /// </summary>
    public string? BaseUrl { get; set; }
}
