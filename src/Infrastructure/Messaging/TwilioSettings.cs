using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Settings for the Twilio messaging integration, bound from the <c>Twilio:</c> configuration
/// section. Values are never hard-coded and the auth token is never logged or returned.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; init; } = string.Empty;
    public string AuthToken { get; init; } = string.Empty;
    public string FromNumber { get; init; } = string.Empty;
    public string MessagingServiceSid { get; init; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim for every
    /// messaging-API call. It does not govern other Twilio hosts (e.g. Lookup).
    /// </summary>
    public string? BaseUrl { get; init; }

    public static TwilioSettings FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        return new TwilioSettings
        {
            AccountSid = section["AccountSid"] ?? string.Empty,
            AuthToken = section["AuthToken"] ?? string.Empty,
            FromNumber = section["FromNumber"] ?? string.Empty,
            MessagingServiceSid = section["MessagingServiceSid"] ?? string.Empty,
            BaseUrl = section["BaseUrl"]
        };
    }
}
