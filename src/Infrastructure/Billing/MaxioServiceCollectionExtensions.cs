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
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName));

        services.AddSingleton<SubscriptionEnrollmentGate>();

        services.AddHttpClient<MaxioApiClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.ApiKey) &&
                (!string.IsNullOrWhiteSpace(options.BaseUrl) || !string.IsNullOrWhiteSpace(options.Subdomain)))
            {
                client.BaseAddress = options.ResolveBaseAddress();
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }

            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });

        services.AddScoped<ISubscriptionBillingService>(sp =>
            ActivatorUtilities.CreateInstance<MaxioSubscriptionBillingService>(sp));
        return services;
    }
}
