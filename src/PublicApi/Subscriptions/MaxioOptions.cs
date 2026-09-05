using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Configuration for the Maxio Advanced Billing API.</summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }

    public Uri GetBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var overriddenBaseUri))
                throw new MaxioConfigurationException("Maxio:BaseUrl must be an absolute URL.");

            return overriddenBaseUri;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
            throw new MaxioConfigurationException("Maxio:Subdomain must be configured when Maxio:BaseUrl is not set.");

        // The bundled Maxio OpenAPI contract's default server is https://{site}.chargify.com.
        if (!Uri.TryCreate($"https://{Subdomain}.chargify.com/", UriKind.Absolute, out var derivedBaseUri))
            throw new MaxioConfigurationException("Maxio:Subdomain cannot be used to derive a valid API URL.");

        return derivedBaseUri;
    }

    public void ValidateForRequest()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new MaxioConfigurationException("Maxio:ApiKey has not been configured.");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new MaxioConfigurationException("Maxio:ProductFamilyHandle has not been configured.");

        _ = GetBaseUri();
    }
}

public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message) { }
}

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddHttpClient<IMaxioBillingClient, MaxioBillingClient>();
        services.AddSingleton<UserSubscriptionLock>();
        return services;
    }
}
