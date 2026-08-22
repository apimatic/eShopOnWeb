using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class TwilioConfiguration
{
    public static void BindFromEnvironment(IConfigurationBuilder configuration)
    {
        var overlay = new Dictionary<string, string?>();
        AddIfPresent(overlay, "TWILIO_ACCOUNT_SID", "Twilio:AccountSid");
        AddIfPresent(overlay, "TWILIO_AUTH_TOKEN", "Twilio:AuthToken");
        AddIfPresent(overlay, "TWILIO_FROM_NUMBER", "Twilio:FromNumber");
        AddIfPresent(overlay, "TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid");

        if (overlay.Count > 0)
        {
            configuration.AddInMemoryCollection(overlay);
        }
    }

    private static void AddIfPresent(IDictionary<string, string?> overlay, string environmentName, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            overlay[configurationKey] = value;
        }
    }
}
