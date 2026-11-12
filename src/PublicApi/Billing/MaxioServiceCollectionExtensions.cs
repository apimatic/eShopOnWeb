using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio SDK client and the billing services. The host refuses to start when a
    /// required <c>Maxio:</c> setting is missing or blank.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioOptions.SectionName);
        services.AddOptions<MaxioOptions>()
            .Configure(o => Resolve(section, o))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Maxio:ApiKey is not configured. Set it via user-secrets or the environment.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Subdomain), "Maxio:Subdomain is not configured. Set it via user-secrets or the environment.")
            .Validate(o => TryParseEnvironment(o.Environment, out _), "Maxio:Environment must be 'US' or 'EU'.")
            .ValidateOnStart();

        // Options are captured once here: a rotated secret takes effect after a restart.
        var settings = new MaxioOptions();
        Resolve(section, settings);

        services.AddMaxioAdvancedBillingClient(options =>
        {
            if (!TryParseEnvironment(settings.Environment, out var environment)
                || string.IsNullOrWhiteSpace(settings.ApiKey)
                || string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                // Validation (ValidateOnStart) refuses to start the host before this client is ever used.
                return;
            }

            options.BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" };
            options.Environment = environment;
            options.Server.Production.Us.Site = settings.Subdomain!;
            options.Server.Production.Eu.Site = settings.Subdomain!;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl!;
            }
        });

        services.AddScoped<MaxioBillingService>();
        return services;
    }

    /// <summary>
    /// Reads the Maxio: section (user-secrets, appsettings, Maxio__* variables). For the three sandbox
    /// settings the deployment supplies as plain environment variables, a blank key falls back to them.
    /// </summary>
    private static void Resolve(IConfigurationSection section, MaxioOptions target)
    {
        target.ApiKey = Pick(section["ApiKey"], "MAXIO_API_KEY");
        target.Subdomain = Pick(section["Subdomain"], "MAXIO_SITE_SUBDOMAIN");
        target.Environment = Pick(section["Environment"], "MAXIO_ENVIRONMENT");
        target.BaseUrl = section["BaseUrl"];
    }

    private static string? Pick(string? configured, string variable)
        => !string.IsNullOrWhiteSpace(configured) ? configured : System.Environment.GetEnvironmentVariable(variable);

    private static bool TryParseEnvironment(string? value, out ServerEnvironment environment)
    {
        environment = ServerEnvironment.Us;
        if (string.Equals(value?.Trim(), "US", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (string.Equals(value?.Trim(), "EU", StringComparison.OrdinalIgnoreCase))
        {
            environment = ServerEnvironment.Eu;
            return true;
        }
        return false;
    }
}
