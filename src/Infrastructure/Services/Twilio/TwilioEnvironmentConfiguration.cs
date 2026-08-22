using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public static class TwilioEnvironmentConfiguration
{
    public static void ApplyEnvironmentOverrides(IConfigurationBuilder configuration)
    {
        var overrides = new Dictionary<string, string?>();
        Copy("TWILIO_ACCOUNT_SID", "Twilio:AccountSid");
        Copy("TWILIO_AUTH_TOKEN", "Twilio:AuthToken");
        Copy("TWILIO_FROM_NUMBER", "Twilio:FromNumber");
        Copy("TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid");

        if (overrides.Count > 0)
        {
            configuration.AddInMemoryCollection(overrides);
        }

        void Copy(string environmentVariable, string configurationKey)
        {
            var value = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrEmpty(value))
            {
                overrides[configurationKey] = value;
            }
        }
    }
}
