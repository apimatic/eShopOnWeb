using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Twilio SMS notification integration: validated settings, the SDK client (with HTTP
    /// basic auth and the optional messaging base-URL override), and the <see cref="ISmsGateway"/>.
    /// </summary>
    public static IServiceCollection AddTwilioNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(TwilioSettings.CONFIG_SECTION);
        var settings = section.Get<TwilioSettings>() ?? new TwilioSettings();

        // A missing credential is a deployment fault, not a request fault: refuse to boot here rather than
        // surfacing it as a provider 401 on the first unlucky request. Names the missing key; never echoes
        // a value.
        GuardRequiredSettings(settings);

        services.Configure<TwilioSettings>(section);

        services.AddTwilioSdkClient(options =>
        {
            options.Environment = ServerEnvironment.Production;

            // Account SID + Auth Token as HTTP basic credentials.
            options.AccountSidAuthToken = new BasicAuthCredentials
            {
                Username = settings.AccountSid,
                Password = settings.AuthToken
            };

            // Bound a single attempt so a hung provider can't pin a request for the 100s default.
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) };

            // Optional override — applies ONLY to the messaging host (Server.Default). The lookups host
            // (Server.Default4) is deliberately left at its own default.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }
        });

        services.AddScoped<ISmsGateway, TwilioSmsGateway>();

        return services;
    }

    private static void GuardRequiredSettings(TwilioSettings settings)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.AccountSid)) missing.Add($"{TwilioSettings.CONFIG_SECTION}:{nameof(TwilioSettings.AccountSid)}");
        if (string.IsNullOrWhiteSpace(settings.AuthToken)) missing.Add($"{TwilioSettings.CONFIG_SECTION}:{nameof(TwilioSettings.AuthToken)}");
        if (string.IsNullOrWhiteSpace(settings.FromNumber)) missing.Add($"{TwilioSettings.CONFIG_SECTION}:{nameof(TwilioSettings.FromNumber)}");
        if (string.IsNullOrWhiteSpace(settings.MessagingServiceSid)) missing.Add($"{TwilioSettings.CONFIG_SECTION}:{nameof(TwilioSettings.MessagingServiceSid)}");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Twilio configuration is incomplete. Missing: {string.Join(", ", missing)}. " +
                "Set these via user-secrets or environment before starting the app.");
        }
    }
}
