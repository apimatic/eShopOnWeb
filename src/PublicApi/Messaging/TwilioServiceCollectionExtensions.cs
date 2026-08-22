using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.PublicApi.Messaging;

public static class TwilioServiceCollectionExtensions
{
    public const string HttpClientName = "Twilio";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));

        var settings = configuration.GetSection(TwilioSettings.SectionName).Get<TwilioSettings>() ?? new TwilioSettings();
        if (!settings.IsConfigured)
        {
            if (environment.IsEnvironment("Testing"))
            {
                services.AddSingleton<ISmsGateway, DisabledSmsGateway>();
                services.AddSingleton<IPhoneNumberLookup, DisabledPhoneNumberLookup>();
                services.AddSingleton<IMessagingSettings>(new TwilioSettings { FromNumber = "+10000000000" });
                return services;
            }

            throw new InvalidOperationException(
                "Twilio:AccountSid, Twilio:AuthToken, Twilio:FromNumber, and Twilio:MessagingServiceSid must be configured before the app starts.");
        }

        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .Validate(s => s.IsConfigured, "Twilio:AccountSid, Twilio:AuthToken, Twilio:FromNumber, and Twilio:MessagingServiceSid must be configured.")
            .ValidateOnStart();

        services.AddTransient<AtMostOneTwilioWriteHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<AtMostOneTwilioWriteHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var bound = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10),
                    MaxRetries = 1
                },
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = bound.AccountSid,
                    Password = bound.AuthToken
                }
            };

            if (!string.IsNullOrWhiteSpace(bound.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = bound.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddSingleton<TwilioSmsGateway>();
        services.AddSingleton<ISmsGateway>(sp => sp.GetRequiredService<TwilioSmsGateway>());
        services.AddSingleton<IPhoneNumberLookup>(sp => sp.GetRequiredService<TwilioSmsGateway>());
        services.AddSingleton<IMessagingSettings>(sp => sp.GetRequiredService<IOptions<TwilioSettings>>().Value);
        return services;
    }
}
