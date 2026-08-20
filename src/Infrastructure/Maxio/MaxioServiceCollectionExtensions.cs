using System;
using System.Net.Http.Headers;
using System.Text;
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
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddSingleton<ISubscriptionIdempotencyLock, InMemorySubscriptionIdempotencyLock>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        services.AddHttpClient<IMaxioBillingClient, MaxioBillingClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
                if (options.IsConfigured)
                {
                    client.BaseAddress = options.GetBaseAddress();
                    var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
                }

                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio/1.0");
            });

        return services;
    }
}
