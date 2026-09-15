using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class TwilioConfiguration
{
    public static void ApplyEnvironmentOverrides(ConfigurationManager configuration)
    {
        var values = new Dictionary<string, string?>();
        Bind("TWILIO_ACCOUNT_SID", "Twilio:AccountSid");
        Bind("TWILIO_AUTH_TOKEN", "Twilio:AuthToken");
        Bind("TWILIO_FROM_NUMBER", "Twilio:FromNumber");
        Bind("TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid");
        Bind("TWILIO_BASE_URL", "Twilio:BaseUrl");

        if (values.Count > 0)
        {
            configuration.AddInMemoryCollection(values);
        }

        void Bind(string envName, string configKey)
        {
            var value = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[configKey] = value;
            }
        }
    }
}
