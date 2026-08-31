using System;
using System.Net.Http;
using CyberSourceMergedSpec;
using CyberSourceMergedSpec.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

public static class InvoicingServiceCollectionExtensions
{
    private const string HttpClientName = "VisaCyberSource";

    /// <summary>
    /// Registers the Visa/CyberSource invoicing integration: a dedicated long-lived
    /// <see cref="System.Net.Http.HttpClient"/>, the SDK client (with the base URL bound verbatim from the
    /// already-registered <see cref="VisaSettings"/>), and the <see cref="IInvoicingService"/> implementation.
    /// The caller is responsible for binding and validating <see cref="VisaSettings"/> first.
    /// </summary>
    public static IServiceCollection AddInvoicingServices(this IServiceCollection services)
    {
        // A dedicated, named HttpClient keeps this SDK's pipeline off the shared default client. The
        // primary handler is pooled so DNS stays fresh behind the long-lived (singleton) client below.
        var httpClientBuilder = services.AddHttpClient(HttpClientName, client =>
            {
                // A backstop only; the real per-attempt bound is RetryOptions.Timeout (set below), and the
                // whole-call bound is the linked deadline the service applies per call.
                client.Timeout = TimeSpan.FromSeconds(120);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Opt-in wire logging for first-run diagnostics; never logs auth headers. Off unless VISA_WIRE_LOG=true.
        if (string.Equals(Environment.GetEnvironmentVariable(VisaWireLogHandler.EnableEnvVar), "true", StringComparison.OrdinalIgnoreCase))
        {
            httpClientBuilder.AddHttpMessageHandler(() => new VisaWireLogHandler());
        }

        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<VisaSettings>>().Value;

            // The SDK's HTTP Signature hook reads its credentials from process environment variables at
            // client construction. Bridge them from configuration (user-secrets / env) *before* constructing
            // the client. Only overwrite when configuration supplies a value, so a deployment that provides
            // them purely through the environment is not clobbered with a blank. The master switch is always
            // set: without it every request would go out unsigned rather than fail.
            Environment.SetEnvironmentVariable("APIMATIC_EXPERIMENTAL_VISA_HTTP_SIGNATURE", "true");
            SetIfPresent("VISA_MERCHANT_ID", settings.MerchantId);
            SetIfPresent("VISA_KEY_ID", settings.KeyId);
            SetIfPresent("VISA_SECRET_KEY", settings.SecretKey);

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new CyberSourceMergedSpecClientOptions
            {
                Retry = RetryOptions.Default() with
                {
                    // Floor of 1 (Polly requires >= 1). Writes (create/issue/withdraw) are POST, so status
                    // retries do not apply; a transport failure can still resend, so keep the exposure minimal.
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds)
                }
            };

            // Route EVERY provider call through the configured base URL, verbatim.
            options.Server.Default.Production.BaseUrl = settings.BaseUrl;

            return new CyberSourceMergedSpecClient(httpClient, options);
        });

        services.AddScoped<IInvoicingService, CyberSourceInvoicingService>();

        return services;
    }

    private static void SetIfPresent(string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
