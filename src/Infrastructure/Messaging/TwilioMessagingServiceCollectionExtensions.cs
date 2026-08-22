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
    public const string HttpClientName = "TwilioSdk";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.AccountSid),
                "Twilio:AccountSid is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.AuthToken),
                "Twilio:AuthToken is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.FromNumber),
                "Twilio:FromNumber is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.MessagingServiceSid),
                "Twilio:MessagingServiceSid is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.")
            .ValidateOnStart();

        services.AddTransient<TwilioSingleWriteHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<TwilioSingleWriteHandler>()
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
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(10)
                },
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddSingleton<ISmsProvider, TwilioSmsProvider>();
        return services;
    }
}
