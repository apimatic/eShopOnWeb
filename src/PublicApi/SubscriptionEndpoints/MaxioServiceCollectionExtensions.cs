using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration: validated options
    /// bound from the Maxio configuration section, the typed Billing API
    /// client (HTTP Basic auth with the API key), and the subscription
    /// orchestration service.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpContextAccessor();

        services.AddHttpClient<IMaxioBillingClient, MaxioBillingClient>((serviceProvider, httpClient) =>
            {
                var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MaxioOptions>>().Value;

                var baseUrl = options.BaseUrl;
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    baseUrl = $"https://{options.Subdomain}.chargify.com/";
                }
                if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
                {
                    baseUrl += "/";
                }
                httpClient.BaseAddress = new Uri(baseUrl, UriKind.Absolute);

                // Billing API authentication: HTTP Basic with the API key as
                // username and "X" as password (see Maxio "Authentication" docs).
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X"));
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                httpClient.Timeout = TimeSpan.FromSeconds(30);
            });

        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}
