using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure;

public static class TwilioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Twilio SDK client (singleton over a named, factory-managed HttpClient)
    /// and the notification gateway. Twilio settings are validated at startup — a missing
    /// credential refuses the boot rather than failing the first request.
    /// </summary>
    public static IServiceCollection AddTwilioNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.ConfigName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient(TwilioNotificationGateway.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>()
                .CreateClient(TwilioNotificationGateway.HttpClientName);

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10)
                },
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Messaging-API override only; the Lookup host (Default4) stays untouched.
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<INotificationGateway, TwilioNotificationGateway>();

        return services;
    }
}
