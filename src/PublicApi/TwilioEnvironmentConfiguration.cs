using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class TwilioEnvironmentConfiguration
{
    public static void ApplyEnvironmentOverrides(IConfigurationBuilder configuration)
    {
        var values = new Dictionary<string, string?>();
        Copy("TWILIO_ACCOUNT_SID", "Twilio:AccountSid", values);
        Copy("TWILIO_AUTH_TOKEN", "Twilio:AuthToken", values);
        Copy("TWILIO_FROM_NUMBER", "Twilio:FromNumber", values);
        Copy("TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid", values);
        Copy("TWILIO_BASE_URL", "Twilio:BaseUrl", values);

        if (values.Count > 0)
            configuration.AddInMemoryCollection(values);
    }

    private static void Copy(string environmentVariable, string configurationKey, Dictionary<string, string?> values)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
            values[configurationKey] = value;
    }
}
