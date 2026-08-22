using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class TwilioConfiguration
{
    internal static IEnumerable<KeyValuePair<string, string?>> EnvironmentOverrides()
    {
        var mappings = new (string EnvName, string ConfigKey)[]
        {
            ("TWILIO_ACCOUNT_SID", "Twilio:AccountSid"),
            ("TWILIO_AUTH_TOKEN", "Twilio:AuthToken"),
            ("TWILIO_FROM_NUMBER", "Twilio:FromNumber"),
            ("TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid"),
            ("TWILIO_BASE_URL", "Twilio:BaseUrl")
        };

        foreach (var (envName, configKey) in mappings)
        {
            var value = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return new KeyValuePair<string, string?>(configKey, value);
            }
        }
    }
}
