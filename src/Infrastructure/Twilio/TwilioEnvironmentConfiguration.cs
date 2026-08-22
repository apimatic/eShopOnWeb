using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioEnvironmentConfiguration
{
    public static IReadOnlyDictionary<string, string?> GetMappedValues()
    {
        var map = new Dictionary<string, string?>();
        Copy(map, "TWILIO_ACCOUNT_SID", "Twilio:AccountSid");
        Copy(map, "TWILIO_AUTH_TOKEN", "Twilio:AuthToken");
        Copy(map, "TWILIO_FROM_NUMBER", "Twilio:FromNumber");
        Copy(map, "TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid");
        Copy(map, "TWILIO_BASE_URL", "Twilio:BaseUrl");
        return map;
    }

    private static void Copy(IDictionary<string, string?> map, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            map[configurationKey] = value;
        }
    }
}
