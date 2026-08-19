using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .PostConfigure(options =>
            {
                options.ApiKey = options.ApiKey?.Trim() ?? string.Empty;
                options.Subdomain = options.Subdomain?.Trim() ?? string.Empty;
                options.ProductFamilyHandle = options.ProductFamilyHandle?.Trim() ?? string.Empty;
                options.BaseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? null : options.BaseUrl.Trim();
            });

        services.AddSingleton<IMaxioSettings>(sp => sp.GetRequiredService<IOptions<MaxioOptions>>().Value);
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        services.AddHttpClient<ISubscriptionBillingGateway, MaxioAdvancedBillingClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
                var baseAddress = options.ResolveApiBaseAddress();
                if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(baseAddress))
                {
                    throw new MaxioConfigurationException(
                        "Maxio is not configured. Set Maxio:ApiKey and either Maxio:BaseUrl or Maxio:Subdomain (from MAXIO_API_KEY / MAXIO_SITE_SUBDOMAIN).");
                }

                client.BaseAddress = new Uri(baseAddress, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(100);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = MaxioAdvancedBillingClient.CreateBasicAuthHeader(options.ApiKey);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        return services;
    }
}
