using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="MaxioOptions"/> from the "Maxio" configuration section and registers
    /// a typed HTTP client for <see cref="IMaxioBillingClient"/>, authenticated via Basic Auth
    /// (API key as username, literal "x" as password) per the Maxio Advanced Billing API.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.CONFIG_NAME));

        services.AddHttpClient<IMaxioBillingClient, MaxioApiClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<MaxioOptions>>().Value;

            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
                ? $"https://{options.Subdomain}.chargify.com"
                : options.BaseUrl!;
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);

            var basicAuthValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuthValue);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}
