using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi;

public static class MaxioServiceExtensions
{
    /// <summary>
    /// Registers Maxio settings (bound from the "Maxio" configuration section) and a
    /// typed <see cref="System.Net.Http.HttpClient"/>-backed <see cref="IMaxioBillingService"/>.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.ConfigSectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey), "Maxio:ApiKey must be configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Subdomain) || !string.IsNullOrWhiteSpace(s.BaseUrl),
                "Maxio:Subdomain (or Maxio:BaseUrl) must be configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProductFamilyHandle), "Maxio:ProductFamilyHandle must be configured.");

        services.AddMemoryCache();

        services.AddHttpClient<IMaxioBillingService, MaxioBillingService>((provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            client.BaseAddress = new Uri(settings.ResolveBaseUrl().TrimEnd('/') + "/");

            // Maxio uses HTTP Basic auth: API key as username, literal "x" as password.
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
