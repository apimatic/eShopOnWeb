using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class TwilioConfigurationExtensions
{
    public static void AddTwilioEnvironmentOverrides(this ConfigurationManager configuration)
    {
        var mappings = new Dictionary<string, string?>();
        Map("TWILIO_ACCOUNT_SID", "Twilio:AccountSid");
        Map("TWILIO_AUTH_TOKEN", "Twilio:AuthToken");
        Map("TWILIO_FROM_NUMBER", "Twilio:FromNumber");
        Map("TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid");
        Map("TWILIO_BASE_URL", "Twilio:BaseUrl");

        if (mappings.Count > 0)
        {
            configuration.AddInMemoryCollection(mappings);
        }

        void Map(string environmentName, string configurationKey)
        {
            var value = System.Environment.GetEnvironmentVariable(environmentName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                mappings[configurationKey] = value;
            }
        }
    }
}
