using Microsoft.Extensions.Configuration;
using Microsoft.eShopWeb.Infrastructure.Twilio;

namespace Microsoft.eShopWeb.PublicApi;

public static class TwilioConfiguration
{
    public static void BindFromEnvironment(IConfiguration configuration)
    {
        Copy(configuration, "TWILIO_ACCOUNT_SID", $"{TwilioOptions.SectionName}:AccountSid");
        Copy(configuration, "TWILIO_AUTH_TOKEN", $"{TwilioOptions.SectionName}:AuthToken");
        Copy(configuration, "TWILIO_FROM_NUMBER", $"{TwilioOptions.SectionName}:FromNumber");
        Copy(configuration, "TWILIO_MESSAGING_SERVICE_SID", $"{TwilioOptions.SectionName}:MessagingServiceSid");
        Copy(configuration, "TWILIO_BASE_URL", $"{TwilioOptions.SectionName}:BaseUrl");
    }

    private static void Copy(IConfiguration configuration, string environmentKey, string configurationKey)
    {
        var value = configuration[environmentKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            value = System.Environment.GetEnvironmentVariable(environmentKey);
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            configuration[configurationKey] = value;
        }
    }
}
