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
    /// Wires up the Maxio Advanced Billing integration. Binds <see cref="MaxioOptions"/> from
    /// the "Maxio" configuration section - populate that section via user-secrets/environment,
    /// never with literal values in source or appsettings.
    /// </summary>
    public static IServiceCollection AddMaxioIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.ConfigSection))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), $"{MaxioOptions.ConfigSection}:ApiKey is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Subdomain) || !string.IsNullOrWhiteSpace(o.BaseUrl),
                $"{MaxioOptions.ConfigSection}:Subdomain (or {MaxioOptions.ConfigSection}:BaseUrl) is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ProductFamilyHandle), $"{MaxioOptions.ConfigSection}:ProductFamilyHandle is required.")
            .ValidateOnStart();

        services.AddHttpClient<MaxioApiClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;

            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
                ? $"https://{options.Subdomain}.chargify.com"
                : options.BaseUrl;
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Maxio Basic Auth: API key as username, literal "X" as password.
            var basicAuthValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuthValue);
        });

        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
