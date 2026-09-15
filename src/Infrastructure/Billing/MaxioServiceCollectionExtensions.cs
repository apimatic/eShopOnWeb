using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddHttpClient<IMaxioBillingClient, MaxioBillingClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var baseUrl = ResolveBaseUrl(options);
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                client.BaseAddress = new Uri(baseUrl);
            }

            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }
        });

        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        return services;
    }

    internal static string? ResolveBaseUrl(MaxioOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.TrimEnd('/') + "/";
        }

        if (!string.IsNullOrWhiteSpace(options.Subdomain))
        {
            return $"https://{options.Subdomain.Trim()}.chargify.com/";
        }

        return null;
    }
}
