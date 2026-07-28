using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registration for the Maxio subscription-billing capability: binds <see cref="MaxioSettings"/>
/// from the <c>Maxio</c> configuration section, constructs the Maxio client, and exposes it through
/// <see cref="ISubscriptionBillingService"/>.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.SectionName);
        services.Configure<MaxioSettings>(section);

        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Provide it via environment variables / .NET user-secrets (never in the repo).");
        }

        if (string.IsNullOrWhiteSpace(settings.Subdomain) && string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvalidOperationException(
                "Maxio requires either Maxio:Subdomain or an explicit Maxio:BaseUrl to be configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");
        }

        var environment = ResolveEnvironment(settings.Environment);

        // The SDK helper registers the client as a singleton over an IHttpClientFactory-managed HttpClient.
        // Options are captured once here (registration time) — the callback may read configuration but not
        // scoped services.
        services.AddMaxioAdvancedBillingClient(options =>
        {
            // HTTP Basic: API key as the username, the literal "x" as the password (SDK convention).
            options.BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" };
            options.Environment = environment;

            // Configure the server node matching the selected environment. The US and EU nodes are
            // distinct nested types, so each branch is set directly. When Maxio:BaseUrl is set it is
            // used verbatim as the API base address (replacing the subdomain-derived template).
            if (environment == ServerEnvironment.Eu)
            {
                if (!string.IsNullOrWhiteSpace(settings.Subdomain))
                {
                    options.Server.Production.Eu.Site = settings.Subdomain;
                }

                if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(settings.Subdomain))
                {
                    options.Server.Production.Us.Site = settings.Subdomain;
                }

                if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    options.Server.Production.Us.BaseUrl = settings.BaseUrl;
                }
            }
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }

    /// <summary>
    /// Maps the configured environment string (e.g. from <c>MAXIO_ENVIRONMENT</c>) onto a
    /// <see cref="ServerEnvironment"/>. Defaults to US. The SDK's environment enum exposes only static
    /// constants, so this mapping is done explicitly rather than via a value-parsing helper.
    /// </summary>
    private static ServerEnvironment ResolveEnvironment(string? environment)
    {
        if (!string.IsNullOrWhiteSpace(environment) &&
            environment.Trim().Equals("EU", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Eu;
        }

        return ServerEnvironment.Us;
    }
}
