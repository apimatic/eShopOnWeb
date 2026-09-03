using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioServiceCollectionExtensions
{
    public const string HttpClientName = "Twilio";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioOptions>()
            .Bind(configuration.GetSection(TwilioOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.AccountSid),
                "Twilio:AccountSid is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.AuthToken),
                "Twilio:AuthToken is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.FromNumber),
                "Twilio:FromNumber is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.MessagingServiceSid),
                "Twilio:MessagingServiceSid is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.")
            .ValidateOnStart();

        services.AddTransient<SingleAttemptWriteHandler>();
        services.AddTransient<TwilioRequestLoggingHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<SingleAttemptWriteHandler>()
            .AddHttpMessageHandler<TwilioRequestLoggingHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
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
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddSingleton<IPhoneNumberLookup, TwilioPhoneNumberLookup>();
        services.AddSingleton<ISmsGateway, TwilioSmsGateway>();

        return services;
    }
}
