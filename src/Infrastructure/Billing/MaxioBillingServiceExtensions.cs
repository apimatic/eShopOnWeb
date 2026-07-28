using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Registers Maxio Advanced Billing: binds the <c>Maxio:</c> settings, configures and registers the
/// SDK client (auth + server/base-URL), and wires the <see cref="ISubscriptionBillingService"/>.
/// </summary>
public static class MaxioBillingServiceExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.SectionName);
        services.Configure<MaxioSettings>(section);
        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        // Options are captured once, at registration (they may read configuration but not scoped services).
        services.AddMaxioAdvancedBillingClient(options =>
        {
            // Basic auth: API key as username, the literal "x" as password.
            options.BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey ?? string.Empty,
                Password = "x",
            };

            var environment = ParseEnvironment(settings.Environment);
            options.Environment = environment;
            ConfigureServer(options, environment, settings);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();
        return services;
    }

    private static ServerEnvironment ParseEnvironment(string? value)
    {
        // Server/environment enums may keep FromValue protected — map explicitly and default to US.
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Equals("EU", StringComparison.OrdinalIgnoreCase))
            return ServerEnvironment.Eu;
        return ServerEnvironment.Us;
    }

    private static void ConfigureServer(MaxioAdvancedBillingClientOptions options, ServerEnvironment environment, MaxioSettings settings)
    {
        var baseUrl = settings.BaseUrl;
        var subdomain = settings.Subdomain;
        var hasBaseUrl = !string.IsNullOrWhiteSpace(baseUrl);

        // Only the selected environment's options are ever read, so configure that one.
        if (environment == ServerEnvironment.Eu)
        {
            if (hasBaseUrl)
                options.Server.Production.Eu.BaseUrl = baseUrl!;
            else if (!string.IsNullOrWhiteSpace(subdomain))
                options.Server.Production.Eu.Site = subdomain!;
        }
        else
        {
            if (hasBaseUrl)
                options.Server.Production.Us.BaseUrl = baseUrl!;
            else if (!string.IsNullOrWhiteSpace(subdomain))
                options.Server.Production.Us.Site = subdomain!;
        }
    }
}
