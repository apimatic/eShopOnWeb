using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class TwilioConfigurationExtensions
{
    public static IConfigurationBuilder AddTwilioEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var mapped = new Dictionary<string, string?>();
        Map(mapped, "TWILIO_ACCOUNT_SID", "Twilio:AccountSid");
        Map(mapped, "TWILIO_AUTH_TOKEN", "Twilio:AuthToken");
        Map(mapped, "TWILIO_FROM_NUMBER", "Twilio:FromNumber");
        Map(mapped, "TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid");

        if (mapped.Count > 0)
        {
            builder.AddInMemoryCollection(mapped);
        }

        return builder;
    }

    private static void Map(IDictionary<string, string?> mapped, string environmentVariable, string configurationKey)
    {
        var value = System.Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            mapped[configurationKey] = value;
        }
    }
}
