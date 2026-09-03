using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Twilio;
using Twilio.Core.Authentication.Basic;
using Twilio.Core.Configuration;
using Twilio.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class TwilioServiceCollectionExtensions
{
    public const string HttpClientName = "Twilio";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .Validate(settings =>
                    !string.IsNullOrWhiteSpace(settings.AccountSid)
                    && !string.IsNullOrWhiteSpace(settings.AuthToken)
                    && !string.IsNullOrWhiteSpace(settings.FromNumber)
                    && !string.IsNullOrWhiteSpace(settings.MessagingServiceSid),
                "Twilio:AccountSid, Twilio:AuthToken, Twilio:FromNumber, and Twilio:MessagingServiceSid must each be set.")
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(20);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var options = new TwilioClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20) },
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                    RedactedHeaders = ["Authorization"],
                    RedactedKeys =
                    [
                        "sig", "signature", "access_token", "apikey", "api_key",
                        "client_secret", "password", "refresh_token", "code", "assertion", "client_assertion",
                        "To", "From", "Body", "AuthToken"
                    ]
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioClient(httpClient, options);
        });

        services.AddScoped<IMessagingGateway, TwilioMessagingGateway>();
        services.AddScoped<IShopperContactService, ShopperContactService>();
        services.AddScoped<IShopperOrderService, ShopperOrderService>();
        return services;
    }
}
