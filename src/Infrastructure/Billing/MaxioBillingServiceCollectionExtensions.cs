using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>
    /// Binds the <c>Maxio:</c> configuration section and registers <see cref="IMaxioBillingService"/>
    /// as a typed <see cref="System.Net.Http.HttpClient"/> pre-configured with the resolved base
    /// address and HTTP Basic authentication (API key as username, literal "X" as password).
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey),
                "Maxio configuration is missing 'Maxio:ApiKey'.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.BaseUrl) || !string.IsNullOrWhiteSpace(s.Subdomain),
                "Maxio configuration requires either 'Maxio:BaseUrl' or 'Maxio:Subdomain'.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProductFamilyHandle),
                "Maxio configuration is missing 'Maxio:ProductFamilyHandle'.");

        services.AddHttpClient<IMaxioBillingService, MaxioBillingService>((provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            client.BaseAddress = settings.ResolveBaseAddress();
            client.Timeout = TimeSpan.FromSeconds(100);

            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}
