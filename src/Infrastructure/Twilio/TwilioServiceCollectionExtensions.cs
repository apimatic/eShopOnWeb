using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Twilio;
using Twilio.Core.Authentication.Basic;
using Twilio.Core.Configuration;
using Twilio.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioServiceCollectionExtensions
{
    private const string HttpClientName = "Twilio";

    /// <summary>
    /// Registers the Twilio messaging client, the <see cref="ISmsGateway"/> that fronts it, and the order
    /// notification services. The caller must bind and validate <see cref="TwilioSettings"/> (the
    /// <c>Twilio:</c> section) separately, so the host fails fast on a missing credential at startup.
    /// </summary>
    public static IServiceCollection AddOrderSmsNotifications(this IServiceCollection services)
    {
        // A dedicated, unshared HTTP pipeline: an explicit per-attempt timeout, and a pooled-connection
        // lifetime so DNS stays fresh behind the long-lived singleton client below.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new TwilioClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                // Disable the SDK's own logging: the lookup number rides in the URL path, so leaving the
                // built-in logger on (or armed via the TWILIOCLIENT_LOG env var) could write a shopper's
                // number to logs. We do our own number-free structured logging in the service layer.
                Logging = new LoggingOptions
                {
                    LoggerFactory = NullLoggerFactory.Instance,
                    LogRequestBody = false
                },
                // Timeout is per attempt; sends are POST and are never resent by the SDK.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) }
            };

            // Twilio:BaseUrl overrides the MESSAGING API host (server group Default) only, verbatim.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl!;
            }

            return new TwilioClient(httpClient, options);
        });

        services.AddScoped<ISmsGateway, TwilioSmsGateway>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
