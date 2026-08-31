using System;
using System.Net.Http;
using CyberSourceMergedSpec;
using CyberSourceMergedSpec.Core.Configuration;
using CyberSourceMergedSpec.Servers;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Wires the Visa/CyberSource invoicing integration into the service container. The base URL is bound from
/// configuration and used verbatim as the base address for every provider call; the credentials are read
/// from configuration (falling back to the ambient environment) and handed to the SDK's HTTP-Signature hook
/// through the environment variables it reads at client construction. No secret is ever written to a log or
/// returned anywhere — this method only moves values it is given into the places the SDK expects them.
/// </summary>
public static class VisaInvoicingRegistration
{
    private const string HttpClientName = "VisaCyberSource";
    private const string SignatureSwitchVariable = "APIMATIC_EXPERIMENTAL_VISA_HTTP_SIGNATURE";

    public static IServiceCollection AddVisaInvoicing(this IServiceCollection services, IConfiguration configuration)
    {
        var baseUrl = configuration["Visa:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Visa:BaseUrl is not configured. Set it to the provider base address every call is routed through.");

        // Credentials: configuration is authoritative; fall back to the ambient environment so the same build
        // runs against a different account without a rebuild. Values are moved, never logged.
        var merchantId = Resolve(configuration, "Visa:MerchantId", "VISA_MERCHANT_ID");
        var keyId = Resolve(configuration, "Visa:KeyId", "VISA_KEY_ID");
        var secretKey = Resolve(configuration, "Visa:SecretKey", "VISA_SECRET_KEY");

        RequirePresent(merchantId, "VISA_MERCHANT_ID / Visa:MerchantId");
        RequirePresent(keyId, "VISA_KEY_ID / Visa:KeyId");
        RequirePresent(secretKey, "VISA_SECRET_KEY / Visa:SecretKey");

        // The SDK's signature hook reads these at client construction; set them before the client is built.
        Environment.SetEnvironmentVariable("VISA_MERCHANT_ID", merchantId);
        Environment.SetEnvironmentVariable("VISA_KEY_ID", keyId);
        Environment.SetEnvironmentVariable("VISA_SECRET_KEY", secretKey);
        Environment.SetEnvironmentVariable(SignatureSwitchVariable, "true");

        var currency = configuration["Visa:Currency"];
        services.AddSingleton(new InvoicingSettings
        {
            Currency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency,
        });

        bool.TryParse(configuration["Visa:DebugHttpLogging"], out var debugLogging);
        services.AddTransient<VisaHttpLoggingHandler>();

        var httpClientBuilder = services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds a single attempt against a hung provider (see the SDK per-attempt retry timeout).
                client.Timeout = TimeSpan.FromSeconds(35);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5), // the client below is a singleton
            });

        if (debugLogging)
            httpClientBuilder.AddHttpMessageHandler<VisaHttpLoggingHandler>();

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = new CyberSourceMergedSpecClientOptions
            {
                Environment = ServerEnvironment.Production,
                // Retries cannot be disabled; keep them at the floor so a transport-level retry cannot
                // silently create a duplicate bill on a write. Reconciliation is the backstop for duplicates.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(30),
                },
            };
            // Route every call through the configured base URL, used verbatim.
            options.Server.Default.Production.BaseUrl = baseUrl;
            return new CyberSourceMergedSpecClient(httpClient, options);
        });

        services.AddScoped<IInvoicingProvider, CyberSourceInvoicingProvider>();
        return services;
    }

    private static string? Resolve(IConfiguration configuration, string configKey, string environmentVariable) =>
        configuration[configKey] is { Length: > 0 } fromConfig
            ? fromConfig
            : Environment.GetEnvironmentVariable(environmentVariable);

    private static void RequirePresent(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"The Visa credential '{name}' is not configured. Set it via user-secrets or the environment; its value is never read from source.");
    }
}
