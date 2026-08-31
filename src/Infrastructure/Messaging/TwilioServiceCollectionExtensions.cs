using System;
using System.Net.Http;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class TwilioServiceCollectionExtensions
{
    private const string HttpClientName = "Twilio";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(TwilioSettings.CONFIG_NAME).Get<TwilioSettings>() ?? new TwilioSettings();

        // A missing credential is a deployment fault — refuse to boot rather than fail
        // the first unlucky request with a 401. Never echo values, configured or not.
        RequireSetting(settings.AccountSid, "Twilio:AccountSid");
        RequireSetting(settings.AuthToken, "Twilio:AuthToken");
        RequireSetting(settings.FromNumber, "Twilio:FromNumber");
        RequireSetting(settings.MessagingServiceSid, "Twilio:MessagingServiceSid");

        services.AddSingleton(settings);

        // A named client keeps this pipeline (timeout, guard handler, pooled lifetime)
        // off the shared default HttpClient.
        services.AddTransient<SingleFlightSendGuard>();
        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds one attempt (backstop for a hung provider); the whole-call
                // budget lives in TwilioMessagingService.
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<SingleFlightSendGuard>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton, so handler rotation never reaches it;
                // keep DNS fresh explicitly.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid!,
                    Password = settings.AuthToken!
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };

            // Optional override for the messaging API only: node "Default" (api.twilio.com)
            // serves every messaging operation; the Lookup node is untouched.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<TwilioMessagingService>();
        services.AddScoped<IMessagingService>(sp => sp.GetRequiredService<TwilioMessagingService>());
        services.AddScoped<IPhoneNumberValidator>(sp => sp.GetRequiredService<TwilioMessagingService>());

        return services;
    }

    private static void RequireSetting(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{key} is not configured. Set it via user-secrets or an environment variable before starting the app.");
        }
    }
}
