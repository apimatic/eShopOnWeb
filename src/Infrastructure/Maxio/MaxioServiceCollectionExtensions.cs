using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var baseUrl = options.ResolveBaseUrl();
            client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }
}
