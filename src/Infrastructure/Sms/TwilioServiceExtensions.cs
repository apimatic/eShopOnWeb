using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

public static class TwilioServiceExtensions
{
    /// <summary>
    /// Registers the Twilio SMS gateway and the order-notification orchestration.
    /// Fails fast at startup when a Twilio credential is missing or blank.
    /// </summary>
    public static IServiceCollection AddOrderSmsNotifications(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(TwilioOptions.SectionName);

        // Fail-fast: the host refuses to start if AccountSid/AuthToken/FromNumber/MessagingServiceSid
        // is missing or blank, rather than discovering it as a 401 on the first call. Each check
        // names its config key and never echoes the value.
        services.AddOptions<TwilioOptions>()
            .Bind(section)
            .Validate(o => !string.IsNullOrWhiteSpace(o.AccountSid),
                "Twilio:AccountSid is not configured. Set it via user-secrets or environment configuration.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.AuthToken),
                "Twilio:AuthToken is not configured. Set it via user-secrets or environment configuration.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.FromNumber),
                "Twilio:FromNumber is not configured. Set it via user-secrets or environment configuration.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.MessagingServiceSid),
                "Twilio:MessagingServiceSid is not configured. Set it via user-secrets or environment configuration.")
            .ValidateOnStart();

        // Options are read once here and captured in the singleton client — a rotated secret takes
        // effect only on process restart.
        var options = section.Get<TwilioOptions>() ?? new TwilioOptions();

        services.AddTwilioSdkClient(o =>
        {
            o.Environment = ServerEnvironment.Production;
            o.AccountSidAuthToken = new BasicAuthCredentials
            {
                Username = options.AccountSid,
                Password = options.AuthToken
            };

            // Twilio:BaseUrl overrides ONLY the messaging API (server group Default). Lookups use a
            // different host (group Default4) and are deliberately left at their default.
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                o.Server.Default.Production.BaseUrl = options.BaseUrl;
            }

            // LogRequestBody stays off (its default). AddTwilioSdkClient assigns LoggerFactory from
            // the container, so the TWILIOCLIENT_LOG env var cannot switch body/PII logging on.
        });

        services.AddScoped<ISmsGateway, TwilioSmsGateway>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
