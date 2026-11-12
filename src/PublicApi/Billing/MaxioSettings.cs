using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>Settings of the "Maxio:" configuration section.</summary>
public class MaxioSettings
{
    public const string SECTION = "Maxio";
    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }
    public string? BaseUrl { get; set; }
}

public static class MaxioRegistration
{
    /// <summary>
    /// Binds the Maxio settings and registers the SDK client and billing service. The host refuses to start
    /// when a required setting is missing or blank. Options are captured once: rotating the key needs a restart.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.SECTION).Get<MaxioSettings>() ?? new MaxioSettings();

        foreach (var (name, value) in new[]
        {
            ("Maxio:ApiKey", settings.ApiKey),
            ("Maxio:Subdomain", settings.Subdomain),
            ("Maxio:Environment", settings.Environment),
        })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Configuration setting '{name}' is missing or blank.");
            }
        }

        ServerEnvironment environment;
        if (string.Equals(settings.Environment!.Trim(), "US", StringComparison.OrdinalIgnoreCase))
        {
            environment = ServerEnvironment.Us;
        }
        else if (string.Equals(settings.Environment.Trim(), "EU", StringComparison.OrdinalIgnoreCase))
        {
            environment = ServerEnvironment.Eu;
        }
        else
        {
            throw new InvalidOperationException("Configuration setting 'Maxio:Environment' must be US or EU.");
        }

        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? null : settings.BaseUrl;
        if (baseUrl != null && !Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Configuration setting 'Maxio:BaseUrl' must be an absolute URL.");
        }

        services.AddMaxioAdvancedBillingClient(options =>
        {
            options.Environment = environment;
            options.BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" };
            options.Server.Production.Us.Site = settings.Subdomain!;
            options.Server.Production.Eu.Site = settings.Subdomain!;
            if (baseUrl != null)
            {
                options.Server.Production.Us.BaseUrl = baseUrl;
                options.Server.Production.Eu.BaseUrl = baseUrl;
            }
            options.Logging = options.Logging with { LogRequestBody = false };
        });
        services.AddScoped<BillingService>();
        return services;
    }
}
