using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioClientOptionsConfigurator
{
    public static void Apply(MaxioAdvancedBillingClientOptions options, MaxioSettings settings)
    {
        var apiKey = string.IsNullOrWhiteSpace(settings.ApiKey) ? "unconfigured" : settings.ApiKey;
        options.BasicAuth = new BasicAuthCredentials
        {
            Username = apiKey,
            Password = "x"
        };

        options.Retry = RetryOptions.Default();

        var environment = ResolveEnvironment(settings.Environment);
        options.Environment = environment;

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
    }

    private static ServerEnvironment ResolveEnvironment(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return ServerEnvironment.Us;
        }

        if (configured.Equals("EU", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Eu;
        }

        if (configured.Equals("US", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Us;
        }

        if (ServerEnvironment.TryGetKnownValue(configured, out var known) && known is not null)
        {
            return known;
        }

        return ServerEnvironment.Us;
    }
}
