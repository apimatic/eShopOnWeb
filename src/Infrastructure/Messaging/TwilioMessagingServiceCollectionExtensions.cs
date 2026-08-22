using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class TwilioMessagingServiceCollectionExtensions
{
    public const string HttpClientName = "TwilioSdk";

    public static IConfigurationBuilder AddTwilioEnvironmentOverlay(this IConfigurationBuilder builder)
    {
        var overlay = new Dictionary<string, string?>();
        Map(overlay, "TWILIO_ACCOUNT_SID", "Twilio:AccountSid");
        Map(overlay, "TWILIO_AUTH_TOKEN", "Twilio:AuthToken");
        Map(overlay, "TWILIO_FROM_NUMBER", "Twilio:FromNumber");
        Map(overlay, "TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid");
        Map(overlay, "TWILIO_BASE_URL", "Twilio:BaseUrl");
        if (overlay.Count > 0)
        {
            builder.AddInMemoryCollection(overlay);
        }

        return builder;
    }

    public static IServiceCollection AddOrderNotifications(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .ValidateDataAnnotations();

        if (!environment.IsEnvironment("Testing"))
        {
            options.ValidateOnStart();
        }

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
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            if (string.IsNullOrWhiteSpace(settings.AccountSid) || string.IsNullOrWhiteSpace(settings.AuthToken))
            {
                throw new InvalidOperationException("Twilio:AccountSid is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.");
            }

            if (string.IsNullOrWhiteSpace(settings.AuthToken))
            {
                throw new InvalidOperationException("Twilio:AuthToken is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.");
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var clientOptions = new TwilioSdkClientOptions
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
                clientOptions.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, clientOptions);
        });

        services.AddSingleton<IMessagingProvider, TwilioMessagingGateway>();
        services.AddScoped<OrderNotificationPublisher>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IShopOrderService, ShopOrderService>();
        services.AddScoped<IOrderFulfillmentService, OrderFulfillmentService>();
        services.AddScoped<INotificationOperatorService, NotificationOperatorService>();
        return services;
    }

    private static void Map(IDictionary<string, string?> overlay, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrEmpty(value))
        {
            overlay[configurationKey] = value;
        }
    }
}
