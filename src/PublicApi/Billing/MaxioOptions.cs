using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings read from the <c>Maxio:</c> configuration section.
/// </summary>
public class MaxioOptions
{
    public const string SECTION_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }
    /// <summary>Optional override. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio SDK client. Refuses to start when the settings are unusable.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioOptions.SECTION_NAME).Get<MaxioOptions>() ?? new MaxioOptions();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured.");
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) && string.IsNullOrWhiteSpace(settings.Subdomain))
            throw new InvalidOperationException("Maxio:Subdomain is not configured (and Maxio:BaseUrl is not set).");

        var environment = (settings.Environment ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "" or "US" => ServerEnvironment.Us,
            "EU" => ServerEnvironment.Eu,
            _ => throw new InvalidOperationException("Maxio:Environment must be 'US' or 'EU'.")
        };

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
            Environment = environment,
            Retry = RetryOptions.Default() with { MaxRetries = 2, Timeout = TimeSpan.FromSeconds(20) },
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl!;
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain!.Trim();
            options.Server.Production.Eu.Site = settings.Subdomain!.Trim();
        }

        // Options are captured once here; a rotated key needs a restart.
        services.AddHttpClient();
        services.AddSingleton(sp =>
            new MaxioAdvancedBillingClient(
                sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient("maxio"), options));
        services.AddScoped<MaxioGateway>();
        return services;
    }
}
