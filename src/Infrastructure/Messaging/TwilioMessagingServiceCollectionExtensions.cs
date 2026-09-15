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

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class TwilioMessagingServiceCollectionExtensions
{
    private const string HttpClientName = "Twilio";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                s => !string.IsNullOrWhiteSpace(s.AccountSid)
                     && !string.IsNullOrWhiteSpace(s.AuthToken)
                     && !string.IsNullOrWhiteSpace(s.FromNumber)
                     && !string.IsNullOrWhiteSpace(s.MessagingServiceSid),
                "Twilio:AccountSid, Twilio:AuthToken, Twilio:FromNumber, and Twilio:MessagingServiceSid are not configured. Set them via environment variable, user-secrets, or your secret store before starting the app.")
            .ValidateOnStart();

        services.AddTransient<LastHttpStatusHandler>();
        services.AddTransient<SingleAttemptWriteHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<LastHttpStatusHandler>()
            .AddHttpMessageHandler<SingleAttemptWriteHandler>()
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
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10),
                    MaxRetries = 1
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsNotificationGateway, TwilioSmsNotificationGateway>();
        return services;
    }
}
