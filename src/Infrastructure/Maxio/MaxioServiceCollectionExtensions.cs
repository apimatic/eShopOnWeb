using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISubscriptionBillingService"/> backed by the Maxio Advanced Billing API,
    /// bound from the "Maxio" configuration section (Maxio:ApiKey, Maxio:Subdomain,
    /// Maxio:ProductFamilyHandle, Maxio:BaseUrl). None of these values are hard-coded here so the
    /// same build can target a different Maxio site/catalog just by changing configuration.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.ConfigSectionName));

        services.AddHttpClient<ISubscriptionBillingService, MaxioSubscriptionBillingService>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;

            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
                ? $"https://{options.Subdomain}.chargify.com/"
                : options.BaseUrl!.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);

            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}
