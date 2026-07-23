using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Registers the billing provider seam. Both hosts call this so the Web storefront and the
/// PublicApi share one implementation and one configuration shape.
/// </summary>
public static class ConfigureBillingServices
{
    /// <summary>
    /// Binds <see cref="MaxioSettings"/> from the "Maxio" configuration section and registers the
    /// single concrete billing client as a typed <see cref="System.Net.Http.HttpClient"/>.
    /// </summary>
    /// <remarks>
    /// The outbound base address comes from <see cref="MaxioSettings.ResolveBaseUrl"/>, so the
    /// same build targets production, a dev/sandbox tenant, or a local mock purely through the
    /// <c>Maxio:BaseUrl</c> setting. The host is never hardcoded here.
    /// </remarks>
    public static IServiceCollection AddBillingServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        services.AddHttpClient<IBillingClient, MaxioBillingClient>((serviceProvider, httpClient) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            httpClient.BaseAddress = new Uri(settings.ResolveBaseUrl());
        });

        return services;
    }
}
