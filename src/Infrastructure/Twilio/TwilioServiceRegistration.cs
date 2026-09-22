using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioServiceRegistration
{
    /// <summary>
    /// Bind <see cref="TwilioSettings"/> (fail-fast at startup), construct the Twilio client with Basic
    /// auth, an optional messaging base-URL override and a bounded per-attempt timeout, and register the
    /// gateway + orchestration service.
    /// </summary>
    public static IServiceCollection AddTwilioOrderNotifications(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Fail-fast: a missing/blank credential stops the host booting rather than 401-ing the first message.
        // Both halves of the Basic credential (AccountSid + AuthToken) and the sender/service are [Required].
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.AccountSid), "Twilio:AccountSid is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.AuthToken), "Twilio:AuthToken is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.FromNumber), "Twilio:FromNumber is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.MessagingServiceSid),
                "Twilio:MessagingServiceSid is not configured.")
            .ValidateOnStart();

        // Options are captured ONCE at registration into the singleton client (documented: a rotated secret
        // needs a process restart).
        var settings = new TwilioSettings();
        configuration.GetSection(TwilioSettings.SectionName).Bind(settings);

        // Keep the singleton client's connections rotating so a DNS change is not cached for the process
        // lifetime (the default IHttpClientFactory client the SDK extension resolves).
        services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddTwilioSdkClient(options =>
        {
            options.Environment = ServerEnvironment.Production;
            options.AccountSidAuthToken = new BasicAuthCredentials
            {
                Username = settings.AccountSid,
                Password = settings.AuthToken
            };

            // Per-attempt timeout (the whole-call deadline is enforced in the gateway). POST writes are not
            // resent by the SDK's default HttpMethodsToRetry, so no duplicate sends.
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) };

            // Sensitive request bodies (destination number + message text): request-body logging stays OFF,
            // and assigning the logger via DI means the TWILIOSDKCLIENT_LOG env var cannot arm it externally.
            options.Logging = options.Logging with { LogRequestBody = false };

            // Optional messaging base-URL override — the Default server group (api.twilio.com), where the
            // Message resource lives. Used verbatim for every messaging call; does NOT govern Lookups.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl!;
            }
        });

        services.AddSingleton<ISmsGateway, TwilioMessagingGateway>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
        services.AddScoped<IShopperContactService, Notifications.ShopperContactService>();
        services.AddScoped<IShopperOrderService, Notifications.ShopperOrderService>();
        services.AddScoped<IOperatorOrderService, Notifications.OperatorOrderService>();

        return services;
    }
}
