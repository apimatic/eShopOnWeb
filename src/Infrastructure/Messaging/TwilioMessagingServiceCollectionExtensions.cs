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

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class TwilioMessagingServiceCollectionExtensions
{
    public const string HttpClientName = "Twilio";

    public static IServiceCollection AddTwilioMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .Validate(
                s => !string.IsNullOrWhiteSpace(s.AccountSid)
                    && !string.IsNullOrWhiteSpace(s.AuthToken)
                    && !string.IsNullOrWhiteSpace(s.FromNumber)
                    && !string.IsNullOrWhiteSpace(s.MessagingServiceSid),
                "Twilio:AccountSid, Twilio:AuthToken, Twilio:FromNumber, and Twilio:MessagingServiceSid must be configured. Set them via environment variables, user-secrets, or your secret store before starting the app.")
            .ValidateOnStart();

        services.AddTransient<SingleAttemptWriteHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<SingleAttemptWriteHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
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

        services.AddSingleton<ITwilioMessagingGateway, TwilioMessagingGateway>();
        services.AddScoped<IShopperContactNumberService, ShopperContactNumberService>();
        services.AddScoped<ICatalogOrderService, CatalogOrderService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
