using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public static class SubscriptionBillingServiceCollectionExtensions
{
    public static IServiceCollection AddSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetRequiredSection(MaxioOptions.SectionName);
        services.AddOptions<MaxioOptions>()
            .Bind(section)
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "Maxio:ApiKey is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.BaseUrl) || !string.IsNullOrWhiteSpace(options.Subdomain),
                "Maxio:Subdomain is required when Maxio:BaseUrl is not set.")
            .Validate(options => MaxioOptions.IsValidBaseUrl(options.ApiBaseUrl), "Maxio:BaseUrl must be an absolute HTTP or HTTPS URL.")
            .ValidateOnStart();

        services.AddHttpClient<IMaxioClient, MaxioClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<ISubscriptionOperationLock, SubscriptionOperationLock>();
        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        return services;
    }
}
