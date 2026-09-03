using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public static class NotificationDependencies
{
    private const string HttpClientName = "TwilioSdk";

    public static IServiceCollection AddOrderNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.AccountSid), "Twilio:AccountSid is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.AuthToken), "Twilio:AuthToken is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.FromNumber), "Twilio:FromNumber is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.MessagingServiceSid), "Twilio:MessagingServiceSid is not configured.")
            .Validate(s => string.IsNullOrWhiteSpace(s.BaseUrl) ||
                Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out _), "Twilio:BaseUrl must be an absolute URL.")
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                Retry = RetryOptions.Disabled() with { Timeout = TimeSpan.FromSeconds(10) },
                Logging = new LoggingOptions
                {
                    LoggerFactory = NullLoggerFactory.Instance,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                    LogRequestBody = false
                },
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                }
            };
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;

            return new TwilioSdkClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName), options);
        });

        services.AddSingleton(sp => sp.GetRequiredService<IOptions<TwilioSettings>>().Value);
        services.AddSingleton<ITwilioMessagingGateway, TwilioMessagingGateway>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
        return services;
    }
}
