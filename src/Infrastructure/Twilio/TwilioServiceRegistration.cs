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

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioServiceRegistration
{
    /// <summary>Named HttpClient for the Twilio SDK — keeps its timeout/handler pipeline off the shared default client.</summary>
    public const string HttpClientName = "Twilio";

    public static void AddTwilioServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Required settings are enforced by the explicit guard in the client factory below,
        // which names the missing key and never echoes a value.
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName));

        services.AddTransient<TwilioSendGuardHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                // Per-attempt backstop against a hung provider (default 100s is an outage, not a timeout).
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .AddHttpMessageHandler<TwilioSendGuardHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton; keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            RequireConfigured(settings.AccountSid, "Twilio:AccountSid");
            RequireConfigured(settings.AuthToken, "Twilio:AuthToken");
            RequireConfigured(settings.FromNumber, "Twilio:FromNumber");
            RequireConfigured(settings.MessagingServiceSid, "Twilio:MessagingServiceSid");

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
                    // Per-attempt bound; the whole-call budget lives in the gateway.
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };

            // Optional messaging-API override, used verbatim. Server group "Default" covers only the
            // messaging API; Lookups (number validation) resolves through its own group and is untouched.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<IPhoneNumberValidator, TwilioPhoneNumberValidator>();
        services.AddScoped<ISmsGateway, TwilioSmsGateway>();
    }

    private static void RequireConfigured(string value, string key)
    {
        // Name the missing key; never echo the value, present or absent.
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{key} is not configured. Set it via user-secrets or environment variables before using the notification features.");
        }
    }
}
