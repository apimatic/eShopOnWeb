using System;
using System.Collections.Generic;
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
    public const string HttpClientName = "Twilio";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .ValidateDataAnnotations()
            .Validate(settings =>
                    !string.IsNullOrWhiteSpace(settings.AccountSid)
                    && !string.IsNullOrWhiteSpace(settings.AuthToken)
                    && !string.IsNullOrWhiteSpace(settings.FromNumber)
                    && !string.IsNullOrWhiteSpace(settings.MessagingServiceSid),
                "Twilio:AccountSid, Twilio:AuthToken, Twilio:FromNumber, and Twilio:MessagingServiceSid must be configured.")
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler(() => new TwilioOnceOnlyWriteHandler())
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            return new TwilioSdkClient(httpClient, CreateClientOptions(settings));
        });

        services.AddScoped<ISmsNotificationClient, TwilioSmsNotificationClient>();
        return services;
    }

    public static TwilioSdkClientOptions CreateClientOptions(TwilioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AccountSid) || string.IsNullOrWhiteSpace(settings.AuthToken))
        {
            throw new InvalidOperationException(
                "Twilio:AccountSid and Twilio:AuthToken are not configured. Set them via environment variable, user-secrets, or your secret store before starting the app.");
        }

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

        return options;
    }

    public static void OverlayTwilioEnvironmentVariables(IConfigurationBuilder configuration)
    {
        var map = new Dictionary<string, string?>();
        Map("TWILIO_ACCOUNT_SID", "Twilio:AccountSid");
        Map("TWILIO_AUTH_TOKEN", "Twilio:AuthToken");
        Map("TWILIO_FROM_NUMBER", "Twilio:FromNumber");
        Map("TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid");
        Map("TWILIO_BASE_URL", "Twilio:BaseUrl");

        if (map.Count > 0)
        {
            configuration.AddInMemoryCollection(map);
        }

        void Map(string envName, string configKey)
        {
            var value = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                map[configKey] = value;
            }
        }
    }
}
