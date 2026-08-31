using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Twilio SDK client (singleton over a named, factory-managed HttpClient),
    /// the ISmsProvider boundary and the order-notification orchestration. Twilio settings are
    /// validated at startup — a missing credential fails the boot, not the first request.
    /// </summary>
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient(TwilioSmsProvider.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30); // per-attempt backstop against a hung provider
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5) // the client below is a singleton
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(TwilioSmsProvider.HttpClientName);

            var options = new TwilioSdkClientOptions
            {
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10) // per attempt
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Messaging API host override only; lookups keep their own default host.
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddSingleton<ISmsProvider, TwilioSmsProvider>(); // stateless wrapper over the singleton SDK client
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
