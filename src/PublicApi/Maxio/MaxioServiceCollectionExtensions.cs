using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new MaxioSettings();
        configuration.GetSection(MaxioSettings.SectionName).Bind(settings);

        // Fall back to the plain environment variables the credentials arrive in,
        // so the same build runs against any Maxio site without appsettings edits.
        settings.ApiKey = FirstNonEmpty(settings.ApiKey, Environment.GetEnvironmentVariable("MAXIO_API_KEY"));
        settings.Subdomain = FirstNonEmpty(settings.Subdomain, Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN"));
        settings.ProductFamilyHandle = FirstNonEmpty(settings.ProductFamilyHandle, Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY"));
        settings.Environment = FirstNonEmpty(
            string.IsNullOrWhiteSpace(settings.Environment) ? null : settings.Environment,
            Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT")) ?? "US";

        services.AddSingleton(settings);

        // Named client keeps the timeout/handler pipeline scoped to the Maxio SDK.
        // Timeout bounds one attempt (the retry pipeline re-arms it per attempt);
        // the whole-call budget lives in MaxioBillingService via CancellationToken.
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            // Validated lazily so hosts that never call the billing endpoints (e.g. test
            // hosts without Maxio config) still boot; the first billing call fails fast.
            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                throw new InvalidOperationException(
                    "Maxio API key is not configured. Set 'Maxio:ApiKey' (user-secrets) or the MAXIO_API_KEY environment variable.");
            }
            if (string.IsNullOrWhiteSpace(settings.BaseUrl) && string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                throw new InvalidOperationException(
                    "Maxio site is not configured. Set 'Maxio:Subdomain' (or MAXIO_SITE_SUBDOMAIN), or provide a 'Maxio:BaseUrl' override.");
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = string.Equals(settings.Environment, "EU", StringComparison.OrdinalIgnoreCase)
                    ? ServerEnvironment.Eu
                    : ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain;
                options.Server.Production.Eu.Site = settings.Subdomain;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();
        return services;
    }

    private static string FirstNonEmpty(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) ? first! : second ?? string.Empty;
}
