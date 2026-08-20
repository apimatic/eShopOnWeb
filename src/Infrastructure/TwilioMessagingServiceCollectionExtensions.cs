using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure;

public static class TwilioMessagingServiceCollectionExtensions
{
    public const string HttpClientName = "TwilioSdk";

    public static IServiceCollection AddTwilioOrderMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        bool validateOnStart = true)
    {
        var options = services
            .AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .ValidateDataAnnotations();

        if (validateOnStart)
        {
            options
                .Validate(
                    settings =>
                        !string.IsNullOrWhiteSpace(settings.AccountSid)
                        && !string.IsNullOrWhiteSpace(settings.AuthToken)
                        && !string.IsNullOrWhiteSpace(settings.FromNumber)
                        && !string.IsNullOrWhiteSpace(settings.MessagingServiceSid),
                    "Twilio:AccountSid, Twilio:AuthToken, Twilio:FromNumber, and Twilio:MessagingServiceSid must be configured.")
                .ValidateOnStart();
        }

        services.AddTransient<TwilioWriteOnceHandler>();
        services.AddTransient<TwilioLastStatusHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<TwilioLastStatusHandler>()
            .AddHttpMessageHandler<TwilioWriteOnceHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var httpClient = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(HttpClientName);
            var clientOptions = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Default(),
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
                clientOptions.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, clientOptions);
        });

        services.AddScoped<ISmsNotificationGateway, TwilioSmsNotificationGateway>();
        services.AddScoped<IShopperContactNumberService, ShopperContactNumberService>();
        services.AddScoped<ICatalogOrderService, CatalogOrderService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
        return services;
    }
}
