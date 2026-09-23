using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Twilio-backed SMS gateway: binds <see cref="TwilioOptions"/> from the <c>Twilio:</c> section
    /// with startup validation (fail-fast on any missing/blank credential part), builds the SDK client once as a
    /// singleton (Basic auth from config, per-attempt timeout, optional messaging base-URL override), and
    /// registers <see cref="ISmsGateway"/>.
    /// </summary>
    public static IServiceCollection AddTwilioSms(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: each credential part is checked individually (a blank part is not a missing one) and the
        // message names the config key without ever echoing its value. ValidateOnStart throws at host start.
        services.AddOptions<TwilioOptions>()
            .Bind(configuration.GetSection(TwilioOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.AccountSid), "Twilio:AccountSid is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.AuthToken), "Twilio:AuthToken is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.FromNumber), "Twilio:FromNumber is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.MessagingServiceSid), "Twilio:MessagingServiceSid is not configured.")
            .ValidateOnStart();

        // Read once at registration to configure the singleton client. A rotated secret takes effect on restart.
        var twilio = configuration.GetSection(TwilioOptions.SectionName).Get<TwilioOptions>() ?? new TwilioOptions();

        services.AddTwilioSdkClient(options =>
        {
            options.Environment = ServerEnvironment.Production;
            options.AccountSidAuthToken = new BasicAuthCredentials
            {
                Username = twilio.AccountSid,
                Password = twilio.AuthToken
            };

            // Timeout is per-attempt; the whole-call budget is enforced by a CancellationToken in the gateway.
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) };

            // Twilio:BaseUrl overrides the messaging API host only (server group Default). Lookup is on
            // another group and keeps its default host.
            if (!string.IsNullOrWhiteSpace(twilio.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = twilio.BaseUrl!;
            }
        });

        services.AddScoped<ISmsGateway, TwilioSmsGateway>();
        return services;
    }
}
