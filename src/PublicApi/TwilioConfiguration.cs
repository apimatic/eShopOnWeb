using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class TwilioConfiguration
{
    public static void ApplyEnvironmentVariables(ConfigurationManager configuration)
    {
        var overrides = new Dictionary<string, string?>();
        AddIfPresent(overrides, "Twilio:AccountSid", "TWILIO_ACCOUNT_SID");
        AddIfPresent(overrides, "Twilio:AuthToken", "TWILIO_AUTH_TOKEN");
        AddIfPresent(overrides, "Twilio:FromNumber", "TWILIO_FROM_NUMBER");
        AddIfPresent(overrides, "Twilio:MessagingServiceSid", "TWILIO_MESSAGING_SERVICE_SID");
        AddIfPresent(overrides, "Twilio:BaseUrl", "TWILIO_BASE_URL");

        if (overrides.Count > 0)
        {
            configuration.AddInMemoryCollection(overrides);
        }
    }

    private static void AddIfPresent(Dictionary<string, string?> overrides, string configKey, string environmentVariable)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            overrides[configKey] = value;
        }
    }
}
