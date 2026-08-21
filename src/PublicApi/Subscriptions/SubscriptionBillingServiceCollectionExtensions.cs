using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal static class SubscriptionBillingServiceCollectionExtensions
{
    internal static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetRequiredSection(MaxioOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "Maxio:ApiKey is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required.")
            .Validate(options =>
                !string.IsNullOrWhiteSpace(options.BaseUrl) || !string.IsNullOrWhiteSpace(options.Subdomain),
                "Maxio:Subdomain is required when Maxio:BaseUrl is not set.")
            .Validate(options => IsValidBaseUrl(options.BaseUrl),
                "Maxio:BaseUrl must be an absolute HTTP or HTTPS URL without a query string or fragment.")
            .ValidateOnStart();

        services.AddHttpClient<IMaxioClient, MaxioClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<AsyncKeyedLocker>();
        services.AddScoped<IShopperIdentityService, ShopperIdentityService>();
        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        return services;
    }

    private static bool IsValidBaseUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return true;
        }

        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
               string.IsNullOrEmpty(uri.Query) &&
               string.IsNullOrEmpty(uri.Fragment);
    }
}
