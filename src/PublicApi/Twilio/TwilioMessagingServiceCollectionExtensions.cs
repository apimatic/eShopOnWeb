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

namespace Microsoft.eShopWeb.PublicApi.Twilio;

public static class TwilioMessagingServiceCollectionExtensions
{
    public const string HttpClientName = "Twilio";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(TwilioSettings.SectionName);
        services.Configure<TwilioSettings>(section);

        var bound = new TwilioSettings();
        section.Bind(bound);

        if (!IsFullyConfigured(bound))
        {
            if (HasAny(bound))
            {
                ThrowMissing(bound);
            }

            services.AddSingleton<ISmsGateway, UnconfiguredSmsGateway>();
            return services;
        }

        services.AddTransient<TwilioOnceWriteHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler<TwilioOnceWriteHandler>();

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10),
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

        services.AddSingleton<ISmsGateway, TwilioSmsGateway>();
        return services;
    }

    private static bool IsFullyConfigured(TwilioSettings settings)
        => !string.IsNullOrWhiteSpace(settings.AccountSid)
           && !string.IsNullOrWhiteSpace(settings.AuthToken)
           && !string.IsNullOrWhiteSpace(settings.FromNumber)
           && !string.IsNullOrWhiteSpace(settings.MessagingServiceSid);

    private static bool HasAny(TwilioSettings settings)
        => !string.IsNullOrWhiteSpace(settings.AccountSid)
           || !string.IsNullOrWhiteSpace(settings.AuthToken)
           || !string.IsNullOrWhiteSpace(settings.FromNumber)
           || !string.IsNullOrWhiteSpace(settings.MessagingServiceSid)
           || !string.IsNullOrWhiteSpace(settings.BaseUrl);

    private static void ThrowMissing(TwilioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AccountSid))
        {
            throw new InvalidOperationException("Twilio:AccountSid is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.");
        }

        if (string.IsNullOrWhiteSpace(settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio:AuthToken is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.");
        }

        if (string.IsNullOrWhiteSpace(settings.FromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.");
        }

        throw new InvalidOperationException("Twilio:MessagingServiceSid is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.");
    }
}
