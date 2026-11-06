using System;
using System.Net;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class BillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio SDK client (configured from the <c>Maxio:</c> section) and the billing service.
    /// The client is built on first use so the app still starts when billing is not configured.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_NAME));

        services.AddMaxioAdvancedBillingClient();
        // Replace the SDK's default registration so settings are applied from configuration.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var options = CreateClientOptions(settings);
            var httpClient = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(string.Empty);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<BillingService>();
        return services;
    }

    public static MaxioAdvancedBillingClientOptions CreateClientOptions(MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new BillingException(HttpStatusCode.ServiceUnavailable, "Billing is not configured: Maxio:ApiKey is missing.");
        }

        var hasBaseUrl = !string.IsNullOrWhiteSpace(settings.BaseUrl);
        if (!hasBaseUrl && string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            throw new BillingException(HttpStatusCode.ServiceUnavailable, "Billing is not configured: Maxio:Subdomain is missing.");
        }

        var environment = ServerEnvironment.Default();
        if (!string.IsNullOrWhiteSpace(settings.Environment))
        {
            var name = settings.Environment.Trim();
            if (string.Equals(name, "US", StringComparison.OrdinalIgnoreCase))
            {
                environment = ServerEnvironment.Us;
            }
            else if (string.Equals(name, "EU", StringComparison.OrdinalIgnoreCase))
            {
                environment = ServerEnvironment.Eu;
            }
            else
            {
                throw new BillingException(HttpStatusCode.ServiceUnavailable, "Billing is not configured: Maxio:Environment must be US or EU.");
            }
        }

        var server = new ServerOptions();
        if (hasBaseUrl)
        {
            // Used verbatim as the API base address.
            server.Production.Us.BaseUrl = settings.BaseUrl!;
            server.Production.Eu.BaseUrl = settings.BaseUrl!;
        }
        else
        {
            server.Production.Us.Site = settings.Subdomain!.Trim();
            server.Production.Eu.Site = settings.Subdomain!.Trim();
        }

        return new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            Server = server,
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" }
        };
    }
}
