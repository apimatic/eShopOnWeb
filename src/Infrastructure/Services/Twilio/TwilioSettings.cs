using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive through user-secrets
/// or environment-specific configuration; none are hard-coded.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string? AccountSid { get; set; }
    public string? AuthToken { get; set; }
    public string? FromNumber { get; set; }
    public string? MessagingServiceSid { get; set; }

    /// <summary>
    /// Optional override for the messaging API base address (the API this integration
    /// sends, reads and reconciles messages through). When set it is used verbatim for
    /// every messaging-API call. Other Twilio capabilities (e.g. Lookup) are served
    /// from other hosts and are not governed by this setting.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string MessagingBaseUrl => string.IsNullOrWhiteSpace(BaseUrl)
        ? "https://api.twilio.com"
        : BaseUrl.TrimEnd('/');

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccountSid) || string.IsNullOrWhiteSpace(AuthToken))
        {
            throw new InvalidOperationException(
                "Twilio messaging is not configured: Twilio:AccountSid and Twilio:AuthToken are required.");
        }

        if (string.IsNullOrWhiteSpace(FromNumber))
        {
            throw new InvalidOperationException(
                "Twilio messaging is not configured: Twilio:FromNumber is required.");
        }
    }
}
