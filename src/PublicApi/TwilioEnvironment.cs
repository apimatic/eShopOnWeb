using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class TwilioEnvironment
{
    public static void Apply(ConfigurationManager configuration)
    {
        var overlay = new Dictionary<string, string?>();
        Map(overlay, "TWILIO_ACCOUNT_SID", "Twilio:AccountSid");
        Map(overlay, "TWILIO_AUTH_TOKEN", "Twilio:AuthToken");
        Map(overlay, "TWILIO_FROM_NUMBER", "Twilio:FromNumber");
        Map(overlay, "TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid");
        Map(overlay, "TWILIO_BASE_URL", "Twilio:BaseUrl");

        if (overlay.Count > 0)
        {
            configuration.AddInMemoryCollection(overlay);
        }
    }

    private static void Map(IDictionary<string, string?> overlay, string environmentName, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            overlay[configurationKey] = value;
        }
    }
}
