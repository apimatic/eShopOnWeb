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

namespace Microsoft.eShopWeb.Infrastructure.TwilioMessaging;

public static class TwilioMessagingServiceCollectionExtensions
{
    public const string HttpClientName = "Twilio";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioOptions>()
            .Bind(configuration.GetSection(TwilioOptions.SectionName))
            .PostConfigure(options =>
            {
                options.AccountSid = FirstNonEmpty(options.AccountSid, configuration["TWILIO_ACCOUNT_SID"]);
                options.AuthToken = FirstNonEmpty(options.AuthToken, configuration["TWILIO_AUTH_TOKEN"]);
                options.FromNumber = FirstNonEmpty(options.FromNumber, configuration["TWILIO_FROM_NUMBER"]);
                options.MessagingServiceSid = FirstNonEmpty(options.MessagingServiceSid, configuration["TWILIO_MESSAGING_SERVICE_SID"]);
                options.BaseUrl = FirstNonEmpty(options.BaseUrl, configuration["TWILIO_BASE_URL"]);
            })
            .Validate(options =>
                !string.IsNullOrWhiteSpace(options.AccountSid)
                && !string.IsNullOrWhiteSpace(options.AuthToken)
                && !string.IsNullOrWhiteSpace(options.FromNumber)
                && !string.IsNullOrWhiteSpace(options.MessagingServiceSid),
                "Twilio:AccountSid, Twilio:AuthToken, Twilio:FromNumber, and Twilio:MessagingServiceSid must be configured.")
            .ValidateOnStart();

        services.AddTransient<AtMostOncePostHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .AddHttpMessageHandler<AtMostOncePostHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var settings = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10),
                    MaxRetries = 2
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

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsGateway, TwilioSmsGateway>();
        return services;
    }

    private static string FirstNonEmpty(string? preferred, string? fallback)
    {
        return !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback ?? string.Empty;
    }
}
