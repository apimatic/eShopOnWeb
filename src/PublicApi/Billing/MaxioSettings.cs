using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Maxio settings, read from the "Maxio" configuration section.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }

    /// <summary>Optional override; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio SDK client and the billing service. Settings are validated lazily
    /// (when the client is first needed) so that the rest of the API keeps working without them.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_NAME));

        services.AddHttpClient();
        services.AddSingleton(sp =>
        {
            var settings = configuration.GetSection(MaxioSettings.CONFIG_NAME).Get<MaxioSettings>() ?? new MaxioSettings();
            var options = BuildOptions(settings);
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<BillingService>();
        return services;
    }

    public static MaxioAdvancedBillingClientOptions BuildOptions(MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new BillingConfigurationException("Maxio:ApiKey is not configured.");
        }

        var environment = ServerEnvironment.Default();
        if (!string.IsNullOrWhiteSpace(settings.Environment))
        {
            environment = settings.Environment.Trim().ToUpperInvariant() switch
            {
                "US" => ServerEnvironment.Us,
                "EU" => ServerEnvironment.Eu,
                _ => throw new BillingConfigurationException("Maxio:Environment must be 'US' or 'EU'.")
            };
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Used as given (a trailing slash is dropped because the SDK appends "/path").
            var baseUrl = settings.BaseUrl.Trim().TrimEnd('/');
            options.Server.Production.Us.BaseUrl = baseUrl;
            options.Server.Production.Eu.BaseUrl = baseUrl;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                throw new BillingConfigurationException("Maxio:Subdomain is not configured.");
            }
            options.Server.Production.Us.Site = settings.Subdomain.Trim();
            options.Server.Production.Eu.Site = settings.Subdomain.Trim();
        }

        return options;
    }
}
