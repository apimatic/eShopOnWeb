using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration: binds the <c>Maxio:</c> settings and wires a
/// typed <see cref="System.Net.Http.HttpClient"/> for <see cref="MaxioClient"/> with the correct
/// base address and HTTP Basic authentication (API key as username, literal "x" as password),
/// exactly as required by the Maxio OpenAPI specification.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey),
                "Maxio:ApiKey must be configured (from the MAXIO_API_KEY secret).")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProductFamilyHandle),
                "Maxio:ProductFamilyHandle must be configured (from MAXIO_DEFAULT_PRODUCT_FAMILY).")
            .Validate(s => !string.IsNullOrWhiteSpace(s.BaseUrl) || !string.IsNullOrWhiteSpace(s.Subdomain),
                "Either Maxio:BaseUrl or Maxio:Subdomain must be configured.");

        services.AddHttpClient<IMaxioSubscriptionService, MaxioClient>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            client.BaseAddress = new Uri(settings.ResolveBaseUrl());

            var basicToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio-Integration/1.0");
        });

        return services;
    }
}
