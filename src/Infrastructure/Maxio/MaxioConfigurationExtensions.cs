using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioConfigurationExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration: options bound from the
    /// "Maxio" configuration section (with environment-variable fallbacks for the
    /// documented MAXIO_* variables), a typed HTTP client, and the subscription service.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .PostConfigure(options =>
            {
                // Fallbacks keyed on the documented environment variable names, so the
                // same build runs against any Maxio site without embedding values.
                options.ApiKey ??= Environment.GetEnvironmentVariable("MAXIO_API_KEY");
                options.Subdomain ??= Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN");
                options.ProductFamilyHandle ??= Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY");
            });

        services.AddHttpClient<IMaxioClient, MaxioClient>((serviceProvider, httpClient) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            httpClient.BaseAddress = new Uri(options.ResolveBaseUrl());
            httpClient.Timeout = TimeSpan.FromSeconds(60);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}