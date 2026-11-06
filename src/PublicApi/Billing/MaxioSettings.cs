using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings of the Maxio integration, bound from the "Maxio:" configuration section.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }

    /// <summary>Optional override; when set it is used verbatim as the API's base address.</summary>
    public string? BaseUrl { get; set; }
}

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_NAME));
        services.AddMaxioAdvancedBillingClient(options =>
        {
            var settings = configuration.GetSection(MaxioSettings.CONFIG_NAME).Get<MaxioSettings>() ?? new MaxioSettings();
            Configure(options, settings);
        });
        services.AddScoped<BillingService>();
        return services;
    }

    public static void Configure(MaxioAdvancedBillingClientOptions options, MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured.");
        if (string.IsNullOrWhiteSpace(settings.Subdomain) && string.IsNullOrWhiteSpace(settings.BaseUrl))
            throw new InvalidOperationException("Maxio:Subdomain is not configured.");

        var environment = string.IsNullOrWhiteSpace(settings.Environment) ? ServerEnvironment.Us.Value : settings.Environment.Trim();
        if (string.Equals(environment, ServerEnvironment.Eu.Value, StringComparison.OrdinalIgnoreCase))
            options.Environment = ServerEnvironment.Eu;
        else if (string.Equals(environment, ServerEnvironment.Us.Value, StringComparison.OrdinalIgnoreCase))
            options.Environment = ServerEnvironment.Us;
        else
            throw new InvalidOperationException("Maxio:Environment must be US or EU.");

        options.BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" };

        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            options.Server.Production.Us.Site = settings.Subdomain!.Trim();
            options.Server.Production.Eu.Site = settings.Subdomain!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl!;
        }
    }
}
