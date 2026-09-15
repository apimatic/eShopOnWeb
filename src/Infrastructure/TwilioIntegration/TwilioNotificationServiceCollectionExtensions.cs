using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.TwilioIntegration;

public static class TwilioNotificationServiceCollectionExtensions
{
    public const string HttpClientName = "Twilio";

    public static IServiceCollection AddTwilioOrderNotifications(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.AccountSid),
                "Twilio:AccountSid is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.AuthToken),
                "Twilio:AuthToken is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.FromNumber),
                "Twilio:FromNumber is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.MessagingServiceSid),
                "Twilio:MessagingServiceSid is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.")
            .ValidateOnStart();

        services.AddTransient<TwilioWriteOnceHandler>();
        services.AddTransient<TwilioLastStatusHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<TwilioWriteOnceHandler>()
            .AddHttpMessageHandler<TwilioLastStatusHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(8),
                    MaxRetries = 1
                },
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsGateway, TwilioSmsGateway>();
        return services;
    }
}
