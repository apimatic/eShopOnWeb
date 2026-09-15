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

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public static class TwilioServiceCollectionExtensions
{
    private const string HttpClientName = "TwilioMessaging";

    /// <summary>
    /// Binds Twilio settings, fails fast if a required credential is missing, and registers a single long-lived
    /// <see cref="TwilioSdkClient"/> over an isolated named <see cref="HttpClient"/> (with a per-attempt timeout,
    /// fresh-DNS pooling, and the duplicate-send guard) plus the <see cref="ISmsSender"/> that fronts it.
    /// </summary>
    public static IServiceCollection AddTwilioSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(TwilioSettings.ConfigSection);
        services.Configure<TwilioSettings>(section);

        // A missing credential is a deployment fault, not a request fault — refuse to boot rather than 401 later.
        ValidateOrThrow(section.Get<TwilioSettings>() ?? new TwilioSettings());

        services.AddTransient<MessageSendGuardHandler>();

        services.AddHttpClient(HttpClientName, c =>
            {
                // Bounds ONE attempt, not the whole call; the orchestration layer applies a whole-operation budget.
                c.Timeout = TimeSpan.FromSeconds(15);
            })
            .AddHttpMessageHandler<MessageSendGuardHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The client is a singleton, so keep DNS from going stale behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Default(),
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) }
            };

            // Twilio:BaseUrl overrides ONLY the messaging (api) host, verbatim. The lookups host is left at its default.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsSender, TwilioSmsSender>();

        return services;
    }

    private static void ValidateOrThrow(TwilioSettings settings)
    {
        RequireValue(settings.AccountSid, $"{TwilioSettings.ConfigSection}:{nameof(TwilioSettings.AccountSid)}");
        RequireValue(settings.AuthToken, $"{TwilioSettings.ConfigSection}:{nameof(TwilioSettings.AuthToken)}");
        RequireValue(settings.FromNumber, $"{TwilioSettings.ConfigSection}:{nameof(TwilioSettings.FromNumber)}");
        RequireValue(settings.MessagingServiceSid, $"{TwilioSettings.ConfigSection}:{nameof(TwilioSettings.MessagingServiceSid)}");
    }

    private static void RequireValue(string value, string key)
    {
        // Name the key, never echo the value (present or absent).
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{key} is not configured. Set it via environment variable or user-secrets before starting the app.");
        }
    }
}
