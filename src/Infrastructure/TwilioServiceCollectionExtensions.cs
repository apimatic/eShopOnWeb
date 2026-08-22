using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Messaging;
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
    public const string HttpClientName = "Twilio";

    public static IServiceCollection AddOrderSmsNotifications(
        this IServiceCollection services,
        IConfiguration configuration,
        bool allowMissingCredentials)
    {
        services.AddOptions<TwilioOptions>()
            .Bind(configuration.GetSection(TwilioOptions.SectionName));

        var settings = configuration.GetSection(TwilioOptions.SectionName).Get<TwilioOptions>() ?? new TwilioOptions();
        var configured = HasRequired(settings);

        if (!configured)
        {
            if (!allowMissingCredentials)
            {
                ThrowMissing(settings);
            }

            services.AddSingleton<ISmsNotificationGateway, UnavailableSmsNotificationGateway>();
            return services;
        }

        services.AddOptions<TwilioOptions>()
            .Bind(configuration.GetSection(TwilioOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.AccountSid), "Twilio:AccountSid is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.AuthToken), "Twilio:AuthToken is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.FromNumber), "Twilio:FromNumber is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.MessagingServiceSid), "Twilio:MessagingServiceSid is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.")
            .ValidateOnStart();

        services.AddTransient<TwilioWriteOnceHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<TwilioWriteOnceHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var bound = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = bound.AccountSid,
                    Password = bound.AuthToken
                },
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };

            if (!string.IsNullOrWhiteSpace(bound.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = bound.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddSingleton<ISmsNotificationGateway, TwilioSmsNotificationGateway>();
        return services;
    }

    private static bool HasRequired(TwilioOptions settings) =>
        !string.IsNullOrWhiteSpace(settings.AccountSid)
        && !string.IsNullOrWhiteSpace(settings.AuthToken)
        && !string.IsNullOrWhiteSpace(settings.FromNumber)
        && !string.IsNullOrWhiteSpace(settings.MessagingServiceSid);

    private static void ThrowMissing(TwilioOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AccountSid))
            throw new InvalidOperationException("Twilio:AccountSid is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.");
        if (string.IsNullOrWhiteSpace(settings.AuthToken))
            throw new InvalidOperationException("Twilio:AuthToken is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.");
        if (string.IsNullOrWhiteSpace(settings.FromNumber))
            throw new InvalidOperationException("Twilio:FromNumber is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.");
        throw new InvalidOperationException("Twilio:MessagingServiceSid is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.");
    }
}
