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
    /// Wires up the Maxio Advanced Billing integration: binds <see cref="MaxioOptions"/> from the
    /// "Maxio" configuration section, registers the underlying typed HttpClient, and registers
    /// <see cref="IMaxioSubscriptionGateway"/>.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.ConfigSectionName));

        services.AddHttpClient<MaxioApiClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;

            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
                ? $"https://{options.Subdomain}.chargify.com"
                : options.BaseUrl.TrimEnd('/');
            client.BaseAddress = new Uri(baseUrl + "/");

            var basicAuthValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuthValue);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddSingleton<MaxioSiteCapabilities>();
        services.AddScoped<IMaxioSubscriptionGateway, MaxioSubscriptionGateway>();

        return services;
    }
}
