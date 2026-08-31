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
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public static class TwilioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Twilio SDK client (singleton over a named, factory-managed
    /// HttpClient) and the application's notification services. The Twilio
    /// configuration section is validated at startup — the app refuses to boot
    /// without its credentials rather than failing on the first request.
    /// </summary>
    public static IServiceCollection AddTwilioNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioOptions>()
            .Bind(configuration.GetSection(TwilioOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient(TwilioSmsProvider.HttpClientName, client =>
            {
                // Per-attempt backstop against a hung provider (default is 100s).
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton: keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(TwilioSmsProvider.HttpClientName);

            var clientOptions = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = options.AccountSid,
                    Password = options.AuthToken
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };

            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                // Verbatim base address for every messaging-API call. Lookup is
                // served from its own host (Server.Default4) and stays untouched.
                clientOptions.Server.Default.Production.BaseUrl = options.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, clientOptions);
        });

        services.AddScoped<ISmsProvider, TwilioSmsProvider>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
