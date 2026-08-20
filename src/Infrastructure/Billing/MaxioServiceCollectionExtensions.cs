using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>().Configure(options =>
        {
            var section = configuration.GetSection(MaxioOptions.SectionName);
            options.ApiKey = section[nameof(MaxioOptions.ApiKey)] ?? string.Empty;
            options.Subdomain = section[nameof(MaxioOptions.Subdomain)] ?? string.Empty;
            options.ProductFamilyHandle = section[nameof(MaxioOptions.ProductFamilyHandle)] ?? string.Empty;
            options.BaseUrl = section[nameof(MaxioOptions.BaseUrl)];
        });

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            if (options.TryGetBaseAddress(out var baseAddress))
            {
                client.BaseAddress = baseAddress;
            }

            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }
        });
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }
}
