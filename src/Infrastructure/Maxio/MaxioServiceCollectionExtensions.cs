using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers Maxio subscription billing: options bound from the "Maxio" configuration
    /// section, a typed <see cref="MaxioClient"/> authenticated via HTTP Basic Auth, and
    /// <see cref="IMaxioSubscriptionService"/>.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.ConfigSection));

        services.AddHttpClient<IMaxioClient, MaxioClient>((serviceProvider, httpClient) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;

            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
                ? $"https://{options.Subdomain}.chargify.com/"
                : options.BaseUrl;
            if (!baseUrl.EndsWith('/'))
            {
                baseUrl += "/";
            }
            httpClient.BaseAddress = new Uri(baseUrl);

            var basicAuthValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuthValue);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
