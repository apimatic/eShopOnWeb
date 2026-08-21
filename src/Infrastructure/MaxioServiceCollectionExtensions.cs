using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure;

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        services.AddHttpClient<IMaxioAdvancedBillingClient, MaxioAdvancedBillingClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var environment = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
            if (options.IsConfigured)
            {
                client.BaseAddress = new Uri(options.ResolveBaseUrl(environment) + "/");
            }
            else
            {
                // Typed client still needs a valid BaseAddress for construction in tests.
                client.BaseAddress = new Uri("https://localhost/");
            }

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
