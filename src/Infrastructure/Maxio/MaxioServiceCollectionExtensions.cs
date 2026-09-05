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
    /// Registers the Maxio Advanced Billing integration, binding <see cref="MaxioOptions"/> from the
    /// "Maxio" configuration section. Configuration is validated lazily, the first time a Maxio call is
    /// made, so hosts that never exercise subscription billing (e.g. the Web project, or unrelated tests)
    /// are unaffected by missing Maxio configuration.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.ConfigSectionName));

        services.AddHttpClient<MaxioApiClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                throw new InvalidOperationException("Maxio:ApiKey configuration is required to call the Maxio API.");
            }

            if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
            {
                throw new InvalidOperationException("Maxio:ProductFamilyHandle configuration is required to call the Maxio API.");
            }

            var baseUrl = !string.IsNullOrWhiteSpace(options.BaseUrl)
                ? options.BaseUrl!.TrimEnd('/')
                : !string.IsNullOrWhiteSpace(options.Subdomain)
                    ? $"https://{options.Subdomain}.chargify.com"
                    : throw new InvalidOperationException("Either Maxio:BaseUrl or Maxio:Subdomain configuration is required to call the Maxio API.");

            client.BaseAddress = new Uri(baseUrl + "/");
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x")));
        });

        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
