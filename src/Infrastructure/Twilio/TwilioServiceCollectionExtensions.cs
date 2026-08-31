using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        // Refuse to boot with missing credentials rather than fail the first request with a 401.
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Named client: keeps this pipeline (timeout, handler lifetime) off the shared default client.
        services.AddHttpClient(TwilioSmsService.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15); // per-attempt backstop against a hung provider
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5) // the SDK client is a singleton; keep DNS fresh
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var options = new TwilioSdkClientOptions
            {
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) }
            };

            // BaseUrl governs the messaging API only (server node Default); lookups are unaffected.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(TwilioSmsService.HttpClientName);
            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsService, TwilioSmsService>();
        return services;
    }
}
