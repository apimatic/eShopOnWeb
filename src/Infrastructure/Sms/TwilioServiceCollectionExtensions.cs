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

namespace Microsoft.eShopWeb.Infrastructure.Sms;

public static class TwilioServiceCollectionExtensions
{
    private const string HttpClientName = "TwilioMessaging";

    /// <summary>
    /// Registers the Twilio SDK client and the SMS notification services. Credentials are bound from
    /// the <c>Twilio:</c> configuration section and validated at startup — a missing one refuses boot.
    /// </summary>
    public static IServiceCollection AddTwilioSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // A named HttpClient keeps this SDK's timeout/handler off the shared default client.
        // Timeout bounds one attempt (default is 100s); PooledConnectionLifetime keeps DNS fresh
        // behind the long-lived singleton client below.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
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
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                // Keep retries minimal so a transport failure on a POST (a real, billable send) cannot
                // re-execute more than once; per-attempt timeout bounds a hung provider.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(15)
                }
            };

            // Optional override, applied only to the messaging (api) host — Lookup is unaffected.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddSingleton<IResendIdempotencyGuard, KeyedResendIdempotencyGuard>();
        services.AddScoped<ISmsNotificationService, TwilioSmsNotificationService>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
