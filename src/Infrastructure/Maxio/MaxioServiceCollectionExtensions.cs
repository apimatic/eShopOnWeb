using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers Maxio-backed subscription billing: binds and validates <see cref="MaxioSettings"/>
    /// from the <c>Maxio:</c> configuration section, wires the typed <see cref="IMaxioClient"/>
    /// HttpClient (base address + HTTP Basic auth), and the <see cref="ISubscriptionService"/>.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey), "Maxio:ApiKey is required.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Subdomain) || !string.IsNullOrWhiteSpace(s.BaseUrl),
                "Either Maxio:Subdomain or Maxio:BaseUrl is required.")
            .ValidateOnStart();

        services.AddHttpClient<IMaxioClient, MaxioClient>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            var baseAddress = settings.ResolveBaseAddress();
            // Ensure a trailing slash so relative request paths resolve under the host root.
            if (!baseAddress.AbsoluteUri.EndsWith('/'))
            {
                baseAddress = new Uri(baseAddress.AbsoluteUri + "/", UriKind.Absolute);
            }

            client.BaseAddress = baseAddress;

            // HTTP Basic: username = API key, password = literal "x" (per the spec's securitySchemes).
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
