using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class TwilioConfigurationExtensions
{
    public static IConfigurationBuilder AddTwilioEnvironmentOverrides(this IConfigurationBuilder configuration)
    {
        var map = new Dictionary<string, string?>();
        Map(map, "TWILIO_ACCOUNT_SID", "Twilio:AccountSid");
        Map(map, "TWILIO_AUTH_TOKEN", "Twilio:AuthToken");
        Map(map, "TWILIO_FROM_NUMBER", "Twilio:FromNumber");
        Map(map, "TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid");
        Map(map, "TWILIO_BASE_URL", "Twilio:BaseUrl");

        if (map.Count > 0)
        {
            configuration.AddInMemoryCollection(map);
        }

        return configuration;
    }

    private static void Map(IDictionary<string, string?> map, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            map[configurationKey] = value;
        }
    }
}
