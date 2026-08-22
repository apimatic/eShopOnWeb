using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.PublicApi.Messaging;

public static class TwilioMessagingServiceCollectionExtensions
{
    public const string HttpClientName = "TwilioMessaging";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var bound = configuration.GetSection(TwilioSettings.SectionName).Get<TwilioSettings>() ?? new TwilioSettings();
        if (string.IsNullOrWhiteSpace(bound.AccountSid))
            throw new InvalidOperationException("Twilio:AccountSid is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.");
        if (string.IsNullOrWhiteSpace(bound.AuthToken))
            throw new InvalidOperationException("Twilio:AuthToken is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.");
        if (string.IsNullOrWhiteSpace(bound.FromNumber))
            throw new InvalidOperationException("Twilio:FromNumber is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.");

        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName));

        services.AddTransient<TwilioWriteOnceHandler>();
        services.AddTransient<TwilioSanitizedLoggingHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<TwilioSanitizedLoggingHandler>()
            .AddHttpMessageHandler<TwilioWriteOnceHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var httpClient = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                Retry = RetryOptions.Default() with { MaxRetries = 1, Timeout = TimeSpan.FromSeconds(10) },
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

        services.AddScoped<ApplicationCore.Interfaces.ISmsNotificationGateway, TwilioSmsNotificationGateway>();
        return services;
    }
}
