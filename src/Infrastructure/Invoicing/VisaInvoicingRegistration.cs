using System;
using System.Net.Http;
using CyberSourceMergedSpec;
using CyberSourceMergedSpec.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Wires the Visa/CyberSource invoicing integration into the service container.
/// </summary>
public static class VisaInvoicingRegistration
{
    private const string HttpClientName = "eShopVisaInvoicing";

    // The env-var names the SDK's HTTP-signature hook reads at client construction. We only reference the
    // names here; values come from configuration (user-secrets / environment), never from source.
    private const string SignatureSwitchEnv = "APIMATIC_EXPERIMENTAL_VISA_HTTP_SIGNATURE";
    private const string MerchantIdEnv = "VISA_MERCHANT_ID";
    private const string KeyIdEnv = "VISA_KEY_ID";
    private const string SecretKeyEnv = "VISA_SECRET_KEY";

    public static IServiceCollection AddVisaInvoicing(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind settings from the Visa section using only the configuration indexer, so no extra binder
        // package is required and secret values are never bound into a logged options graph accidentally.
        var section = VisaSettings.SectionName;
        var settings = new VisaSettings
        {
            BaseUrl = configuration[$"{section}:BaseUrl"],
            MerchantId = configuration[$"{section}:MerchantId"],
            KeyId = configuration[$"{section}:KeyId"],
            SecretKey = configuration[$"{section}:SecretKey"],
            RequestTimeoutSeconds = int.TryParse(configuration[$"{section}:RequestTimeoutSeconds"], out var t) && t > 0 ? t : 30,
            LogWire = bool.TryParse(configuration[$"{section}:LogWire"], out var lw) && lw
        };

        ConfigureAuthentication(settings);

        services.AddSingleton(Options.Create(settings));

        services.AddTransient<SingleSendGuardHandler>();
        services.AddTransient<VisaWireLogHandler>();

        // A named HttpClient scoped to this SDK: its own timeout (a backstop for a hung attempt), the
        // single-send guard for non-idempotent writes, and a pooled-connection lifetime so DNS stays fresh
        // behind the long-lived (singleton) client below.
        var httpClientBuilder = services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds + 5);
            })
            .AddHttpMessageHandler<SingleSendGuardHandler>();

        if (settings.LogWire)
        {
            httpClientBuilder.AddHttpMessageHandler<VisaWireLogHandler>();
        }

        httpClientBuilder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = new CyberSourceMergedSpecClientOptions
            {
                // A modest per-attempt timeout; the provider wrapper additionally puts one whole-call budget
                // on every operation. POST writes are not status-retried by default, and the single-send
                // guard covers the transport-retry double-bill hazard.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 2,
                    Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds)
                }
            };

            // Route every provider call through the configured base URL verbatim. When unset, the SDK's own
            // default (its sandbox host) applies.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new CyberSourceMergedSpecClient(httpClient, options);
        });

        services.AddScoped<IInvoicingProvider, VisaInvoicingProvider>();

        return services;
    }

    /// <summary>
    /// Resolves the three credentials from configuration (falling back to any already-present environment
    /// variable), fails fast and clearly if one is missing, and sets the environment variables the SDK's
    /// signature hook reads — before any client is constructed. Values are never logged.
    /// </summary>
    private static void ConfigureAuthentication(VisaSettings settings)
    {
        var merchantId = FirstNonEmpty(settings.MerchantId, Environment.GetEnvironmentVariable(MerchantIdEnv));
        var keyId = FirstNonEmpty(settings.KeyId, Environment.GetEnvironmentVariable(KeyIdEnv));
        var secretKey = FirstNonEmpty(settings.SecretKey, Environment.GetEnvironmentVariable(SecretKeyEnv));

        RequirePresent(merchantId, MerchantIdEnv, $"{VisaSettings.SectionName}:MerchantId");
        RequirePresent(keyId, KeyIdEnv, $"{VisaSettings.SectionName}:KeyId");
        RequirePresent(secretKey, SecretKeyEnv, $"{VisaSettings.SectionName}:SecretKey");

        Environment.SetEnvironmentVariable(MerchantIdEnv, merchantId);
        Environment.SetEnvironmentVariable(KeyIdEnv, keyId);
        Environment.SetEnvironmentVariable(SecretKeyEnv, secretKey);
        Environment.SetEnvironmentVariable(SignatureSwitchEnv, "true");
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void RequirePresent(string? value, string envName, string configKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Visa invoicing credential is not configured. Set '{configKey}' (user-secrets) or the '{envName}' environment variable.");
        }
    }
}
