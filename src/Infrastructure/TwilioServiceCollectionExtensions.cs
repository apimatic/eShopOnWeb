using System;
using System.Collections.Generic;
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
    public const string HttpClientName = "TwilioSdk";

    public static IServiceCollection AddTwilioMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName));

        services.AddSingleton<IOrderNotificationSettings>(sp => sp.GetRequiredService<IOptions<TwilioSettings>>().Value);

        if (string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<ISmsNotificationGateway, DisabledSmsNotificationGateway>();
            return services;
        }

        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .Validate(s =>
                    !string.IsNullOrWhiteSpace(s.AccountSid)
                    && !string.IsNullOrWhiteSpace(s.AuthToken)
                    && !string.IsNullOrWhiteSpace(s.FromNumber)
                    && !string.IsNullOrWhiteSpace(s.MessagingServiceSid),
                "Twilio:AccountSid, Twilio:AuthToken, Twilio:FromNumber, and Twilio:MessagingServiceSid must be configured.")
            .ValidateOnStart();

        services.AddTransient<TwilioWriteOnceHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .AddHttpMessageHandler<TwilioWriteOnceHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

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
                }
            };

            options.AccountSidAuthToken = new BasicAuthCredentials
            {
                Username = settings.AccountSid,
                Password = settings.AuthToken
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

    public static void AddTwilioEnvironmentOverrides(IConfigurationBuilder configuration)
    {
        var map = new Dictionary<string, string?>();
        Copy(map, "TWILIO_ACCOUNT_SID", "Twilio:AccountSid");
        Copy(map, "TWILIO_AUTH_TOKEN", "Twilio:AuthToken");
        Copy(map, "TWILIO_FROM_NUMBER", "Twilio:FromNumber");
        Copy(map, "TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid");
        Copy(map, "TWILIO_BASE_URL", "Twilio:BaseUrl");

        if (map.Count > 0)
        {
            configuration.AddInMemoryCollection(map);
        }
    }

    private static void Copy(IDictionary<string, string?> map, string envName, string configKey)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            map[configKey] = value;
        }
    }
}
