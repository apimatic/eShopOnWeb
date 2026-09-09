using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Registers the Maxio Advanced Billing client and the subscription service.
    /// Values come from the "Maxio" configuration section (user-secrets), with the
    /// documented MAXIO_* environment variables as a fallback so the same build can be
    /// pointed at a different Maxio site without code changes.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(options =>
        {
            configuration.GetSection(MaxioOptions.SectionName).Bind(options);
            options.ApiKey = FirstNonEmpty(options.ApiKey, Environment.GetEnvironmentVariable("MAXIO_API_KEY"));
            options.Subdomain = FirstNonEmpty(options.Subdomain, Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN"));
            options.ProductFamilyHandle = FirstNonEmpty(options.ProductFamilyHandle, Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY"));
            options.BaseUrl = FirstNonEmpty(options.BaseUrl, Environment.GetEnvironmentVariable("MAXIO_BASE_URL"));
        });

        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds one attempt; the per-call total is bounded by the service's token budget.
                client.Timeout = AttemptTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MaxioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                Retry = RetryOptions.Default() with { Timeout = AttemptTimeout },
                BasicAuth = new BasicAuthCredentials
                {
                    Username = options.ApiKey,
                    Password = "x"
                }
            };

            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl;
            }
            else
            {
                clientOptions.Server.Production.Us.Site = options.Subdomain;
            }

            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    private static string FirstNonEmpty(string? primary, string? fallback) =>
        !string.IsNullOrWhiteSpace(primary) ? primary : (fallback ?? string.Empty);
}
