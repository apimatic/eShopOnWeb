using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class TwilioConfigurationExtensions
{
    public static void AddTwilioEnvironmentOverrides(this ConfigurationManager configuration)
    {
        var overrides = new Dictionary<string, string?>();
        Map(overrides, "TWILIO_ACCOUNT_SID", "Twilio:AccountSid");
        Map(overrides, "TWILIO_AUTH_TOKEN", "Twilio:AuthToken");
        Map(overrides, "TWILIO_FROM_NUMBER", "Twilio:FromNumber");
        Map(overrides, "TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid");
        Map(overrides, "TWILIO_BASE_URL", "Twilio:BaseUrl");

        if (overrides.Count > 0)
        {
            configuration.AddInMemoryCollection(overrides);
        }
    }

    private static void Map(IDictionary<string, string?> target, string environmentName, string configurationKey)
    {
        var value = System.Environment.GetEnvironmentVariable(environmentName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[configurationKey] = value;
        }
    }
}
