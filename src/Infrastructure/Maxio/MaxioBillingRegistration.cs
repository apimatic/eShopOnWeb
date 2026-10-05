using System;
using System.Net;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration: options binding with fail-fast validation,
/// a long-lived SDK client over a named <c>IHttpClientFactory</c> client, and the billing service.
/// </summary>
public static class MaxioBillingRegistration
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddHttpClient(HttpClientName, client =>
        {
            // Per-attempt backstop. The SDK's own Retry.Timeout (30s) fires first; this bounds an
            // attempt if the SDK timeout is ever disabled.
            client.Timeout = TimeSpan.FromSeconds(35);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });

        services.AddSingleton(sp =>
        {
            // Built and validated ONCE, on first resolution, and held for the process lifetime —
            // a rotated secret takes effect on process restart only. Validating here (rather than
            // at registration) keeps hosts that never touch billing — e.g. test hosts building
            // Program.cs without Maxio configuration — bootable, while any billing call still
            // fails fast with the missing key named before a single request is sent.
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MaxioOptions>>().Value;
            Validate(options);
            var clientOptions = BuildClientOptions(options);
            // LoggerFactory is assigned explicitly so the MAXIOADVANCEDBILLINGCLIENT_LOG environment
            // variable cannot arm request/response (body) logging from outside the code.
            clientOptions.Logging = new LoggingOptions
            {
                LoggerFactory = sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>(),
                LogRequestHeaders = false,
                LogResponseHeaders = false,
                LogRequestBody = false
            };
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        services.AddScoped<IMaxioBillingService, MaxioBillingService>();
        return services;
    }

    internal static void Validate(MaxioOptions options)
    {
        // Name the missing key; never echo the value.
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException(
                $"{MaxioOptions.SectionName}:ApiKey is not configured. Set it via user-secrets or the MAXIO_API_KEY environment variable before starting the app.");

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
            throw new InvalidOperationException(
                $"{MaxioOptions.SectionName}:ProductFamilyHandle is not configured. Set it via user-secrets or the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable before starting the app.");

        if (string.IsNullOrWhiteSpace(options.Subdomain) && string.IsNullOrWhiteSpace(options.BaseUrl))
            throw new InvalidOperationException(
                $"{MaxioOptions.SectionName}:Subdomain is not configured (and no {MaxioOptions.SectionName}:BaseUrl override is set). " +
                "Set it via user-secrets or the MAXIO_SITE_SUBDOMAIN environment variable before starting the app.");

        if (string.IsNullOrWhiteSpace(options.Environment))
            return; // defaults to US

        var normalized = options.Environment.Trim();
        if (!normalized.Equals("US", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Equals("EU", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{MaxioOptions.SectionName}:Environment must be \"US\" or \"EU\"; it was set to an unrecognized value.");
    }

    private static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioOptions options)
    {
        var environment = string.IsNullOrWhiteSpace(options.Environment) || options.Environment.Trim().Equals("US", StringComparison.OrdinalIgnoreCase)
            ? ServerEnvironment.Us
            : ServerEnvironment.Eu;

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            // Per-attempt timeout: a hung attempt is retried on retryable verbs, so this is not a
            // total bound — the service passes a deadline CancellationToken around every call.
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) },
            BasicAuth = new BasicAuthCredentials { Username = options.ApiKey!, Password = "x" },
            Server = new ServerOptions()
        };

        var production = clientOptions.Server.Production;
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            // Verbatim base-address override: used as-is instead of the subdomain-derived address.
            if (environment == ServerEnvironment.Eu)
                production.Eu.BaseUrl = options.BaseUrl;
            else
                production.Us.BaseUrl = options.BaseUrl;
        }
        else
        {
            if (environment == ServerEnvironment.Eu)
                production.Eu.Site = options.Subdomain!;
            else
                production.Us.Site = options.Subdomain!;
        }

        return clientOptions;
    }
}