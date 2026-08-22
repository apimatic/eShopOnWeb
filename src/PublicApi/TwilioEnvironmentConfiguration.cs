using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class TwilioEnvironmentConfiguration
{
    public static void Apply(ConfigurationManager configuration)
    {
        Map(configuration, "TWILIO_ACCOUNT_SID", "Twilio:AccountSid");
        Map(configuration, "TWILIO_AUTH_TOKEN", "Twilio:AuthToken");
        Map(configuration, "TWILIO_FROM_NUMBER", "Twilio:FromNumber");
        Map(configuration, "TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid");
        Map(configuration, "TWILIO_BASE_URL", "Twilio:BaseUrl");
    }

    private static void Map(ConfigurationManager configuration, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            configuration[configurationKey] = value;
        }
    }
}
