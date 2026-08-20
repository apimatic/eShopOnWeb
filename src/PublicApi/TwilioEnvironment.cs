using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class TwilioEnvironment
{
    public static void Apply(IConfiguration configuration)
    {
        SetIfPresent(configuration, "TWILIO_ACCOUNT_SID", "Twilio:AccountSid");
        SetIfPresent(configuration, "TWILIO_AUTH_TOKEN", "Twilio:AuthToken");
        SetIfPresent(configuration, "TWILIO_FROM_NUMBER", "Twilio:FromNumber");
        SetIfPresent(configuration, "TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid");
    }

    private static void SetIfPresent(IConfiguration configuration, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            configuration[configurationKey] = value;
        }
    }
}
