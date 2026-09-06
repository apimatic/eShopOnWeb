using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioBillingExtensions
{
    /// <summary>
    /// Environment variables the sandbox credentials arrive in, mapped onto the configuration keys
    /// the integration binds. Only the names live here - the values stay in the environment or in
    /// user-secrets and never in the repository.
    /// </summary>
    private static readonly (string EnvironmentVariable, string ConfigurationKey)[] EnvironmentFallbacks =
    {
        ("MAXIO_API_KEY", "Maxio:ApiKey"),
        ("MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain"),
        ("MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle"),
        ("MAXIO_BASE_URL", "Maxio:BaseUrl")
    };

    /// <summary>
    /// Fills in any Maxio setting that configuration does not already provide from the well-known
    /// MAXIO_* environment variables. Anything already configured - user-secrets in particular -
    /// wins, so this is a convenience for a fresh clone, not an override.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentFallback(this IConfigurationManager configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var fallbacks = new Dictionary<string, string?>();
        foreach (var (variable, key) in EnvironmentFallbacks)
        {
            if (!string.IsNullOrWhiteSpace(configuration[key])) continue;

            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                fallbacks[key] = value;
            }
        }

        if (fallbacks.Count > 0)
        {
            configuration.AddInMemoryCollection(fallbacks);
        }

        return configuration;
    }

    /// <summary>
    /// Registers the Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
    ///
    /// Settings are validated lazily rather than at start-up on purpose: a host that is not
    /// configured for billing (the catalog-only integration test host, for example) must still
    /// boot, and only the subscription endpoints should fail - with a message naming the setting
    /// that is missing.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MaxioOptions>().Bind(configuration.GetSection(MaxioOptions.SectionName));
        services.AddSingleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>();

        services.AddTransient<MaxioRetryHandler>();
        services.AddHttpClient<MaxioApiClient>((serviceProvider, http) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;

                http.BaseAddress = options.ResolveBaseAddress();
                http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

                // Maxio uses HTTP Basic auth over TLS with the API key as the user name and the
                // literal "X" as the password.
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ApiKey}:X"));
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                http.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");
            })
            .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddSingleton<MaxioSiteContext>();
        services.AddSingleton<KeyedAsyncLock>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
