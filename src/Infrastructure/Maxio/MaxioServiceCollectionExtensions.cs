using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registration for the Maxio Advanced Billing integration.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="MaxioOptions"/> from the <c>Maxio</c> configuration section and
    /// registers the typed API client and the subscription-billing service. The client's
    /// base address and HTTP Basic authorization come from the spec's server template and
    /// auth scheme; secrets are read from configuration (user-secrets), never the repo.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Manual binding (via the configuration indexer) keeps this method free of the
        // configuration-binder dependency and works uniformly across hosts.
        var section = configuration.GetSection(MaxioOptions.SectionName);
        var options = new MaxioOptions
        {
            ApiKey = section["ApiKey"],
            Subdomain = section["Subdomain"],
            ProductFamilyHandle = section["ProductFamilyHandle"],
            BaseUrl = section["BaseUrl"]
        };
        options.Validate();

        services.AddSingleton(options);

        // HTTP Basic auth: username = API key, password = "x" (BasicAuth security scheme).
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));

        services.AddHttpClient<IMaxioClient, MaxioClient>(client =>
        {
            client.BaseAddress = options.ResolveBaseAddress();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }
}
