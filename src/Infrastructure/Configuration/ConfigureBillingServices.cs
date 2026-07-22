using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Wires the subscription feature into a host: the typed Maxio options, the typed HttpClient that
/// resolves its target server from configuration, and the ApplicationCore service over it.
/// </summary>
public static class ConfigureBillingServices
{
    public static IServiceCollection AddSubscriptionServices(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.CONFIG_SECTION);
        services.Configure<MaxioSettings>(section);

        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        // Fail fast: a host that cannot reach the provider, or has no credentials, must not boot
        // and then discover it on the first customer request.
        settings.Validate();

        services.AddSingleton(new SubscriptionSettings
        {
            ProductFamilyHandle = settings.ProductFamilyHandle,
            DefaultProductHandle = settings.DefaultProductHandle,
            AlternateProductHandle = settings.AlternateProductHandle,
            MeteredComponentHandle = settings.MeteredComponentHandle
        });

        services.AddTransient<MaxioAuthenticationHandler>();

        // The BaseAddress is resolved from configuration - an explicit Maxio:BaseUrl wins, otherwise
        // the host is derived from the subdomain - so the same build can target production, a dev
        // tenant, or a local mock server without a code change.
        services.AddHttpClient<IBillingClient, MaxioBillingClient>((serviceProvider, httpClient) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            httpClient.BaseAddress = new Uri(options.ResolveBaseUrl());
            httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        })
        .AddHttpMessageHandler<MaxioAuthenticationHandler>();

        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}
