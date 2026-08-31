using System;
using System.Net.Http;
using CyberSourceMergedSpec;
using CyberSourceMergedSpec.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

public static class InvoicingServiceCollectionExtensions
{
    private const string HttpClientName = "Visa";

    // Names of the environment variables the SDK's opt-in HTTP Signature hook reads (once, at client
    // construction). The values come from configuration; the switch must be exactly "true" to activate.
    private const string SignatureSwitchVar = "APIMATIC_EXPERIMENTAL_VISA_HTTP_SIGNATURE";
    private const string MerchantIdVar = "VISA_MERCHANT_ID";
    private const string KeyIdVar = "VISA_KEY_ID";
    private const string SecretKeyVar = "VISA_SECRET_KEY";

    /// <summary>
    /// Registers the Visa/CyberSource invoicing integration: the SDK client (routed through
    /// <c>Visa:BaseUrl</c> from configuration), the provider adapter, and the invoicing/order-placement
    /// application services.
    /// </summary>
    public static IServiceCollection AddVisaInvoicing(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(VisaSettings.ConfigSection).Get<VisaSettings>() ?? new VisaSettings();

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvalidOperationException(
                $"'{VisaSettings.ConfigSection}:{nameof(VisaSettings.BaseUrl)}' must be configured — every Visa call is routed through it.");
        }

        // Bridge the configured credentials into the environment the signing hook reads, and turn the hook
        // on. Only set a variable when configuration actually supplies it, so an already-present environment
        // value is never clobbered with a blank. This runs before the client is ever constructed.
        Environment.SetEnvironmentVariable(SignatureSwitchVar, "true");
        SetIfProvided(MerchantIdVar, settings.MerchantId);
        SetIfProvided(KeyIdVar, settings.KeyId);
        SetIfProvided(SecretKeyVar, settings.SecretKey);

        // A named HttpClient keeps this SDK's timeout/handler off the shared default client. The per-attempt
        // timeout bounds a hang; PooledConnectionLifetime keeps DNS fresh behind the singleton client.
        var httpClientBuilder = services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(40))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Opt-in wire logging for first-run verification of a new call (VISA_WIRE_LOG=true). Never logs the
        // signing headers or the secret.
        if (VisaWireLogHandler.Enabled)
        {
            httpClientBuilder.AddHttpMessageHandler(() => new VisaWireLogHandler());
        }

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = new CyberSourceMergedSpecClientOptions
            {
                // Route EVERY call through the configured base URL, verbatim, in place of the SDK default.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30), MaxRetries = 2 }
            };
            options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            return new CyberSourceMergedSpecClient(httpClient, options);
        });

        services.AddScoped<IInvoiceProvider, VisaInvoiceProvider>();
        services.AddScoped<IInvoicingService, InvoicingService>();
        services.AddScoped<IOrderPlacementService, OrderPlacementService>();

        return services;
    }

    private static void SetIfProvided(string variable, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Environment.SetEnvironmentVariable(variable, value);
        }
    }
}
