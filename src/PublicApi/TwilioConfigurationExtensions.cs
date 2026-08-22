using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class TwilioConfigurationExtensions
{
    public static void AddTwilioEnvironmentVariables(this ConfigurationManager configuration)
    {
        var mapped = new Dictionary<string, string?>();
        Map("TWILIO_ACCOUNT_SID", "Twilio:AccountSid");
        Map("TWILIO_AUTH_TOKEN", "Twilio:AuthToken");
        Map("TWILIO_FROM_NUMBER", "Twilio:FromNumber");
        Map("TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid");

        if (mapped.Count > 0)
        {
            configuration.AddInMemoryCollection(mapped);
        }

        void Map(string environmentName, string configurationKey)
        {
            var value = Environment.GetEnvironmentVariable(environmentName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                mapped[configurationKey] = value;
            }
        }
    }
}
