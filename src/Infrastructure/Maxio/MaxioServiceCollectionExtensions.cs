using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Wires up subscription billing backed by Maxio Advanced Billing.</summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISubscriptionService"/> and its Maxio transport, binding options from the
    /// <c>Maxio:</c> configuration section.
    /// </summary>
    /// <remarks>
    /// Registration deliberately does not fail when the section is absent: the host must still start
    /// so that the rest of the API - and the deployment's health checks - keep working. Incomplete
    /// configuration is reported the first time billing is actually used, as a
    /// <c>BillingNotConfiguredException</c> that names the missing keys.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.ConfigurationSectionName));

        services.AddMemoryCache();
        services.AddTransient<MaxioTransientFaultHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>(ConfigureClient)
            .AddHttpMessageHandler<MaxioTransientFaultHandler>();

        // Scoped, not singleton: it depends on the typed HttpClient, and holding one of those for the
        // lifetime of the process defeats the handler rotation IHttpClientFactory exists to provide.
        // The cached values themselves live in the singleton IMemoryCache.
        services.AddScoped<IMaxioCatalogCache, MaxioCatalogCache>();
        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    private static void ConfigureClient(IServiceProvider serviceProvider, HttpClient client)
    {
        var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

        if (!settings.IsConfigured)
        {
            // Leave the client unconfigured rather than failing to construct it. Resolving a service
            // must not throw, or an unrelated request that merely injects it would fail in the
            // pipeline; MaxioSubscriptionService reports the missing keys when billing is actually
            // used, as a BillingNotConfiguredException naming each of them.
            return;
        }

        client.BaseAddress = settings.ResolveBaseAddress();
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // BasicAuth security scheme: "The username is a Maxio Chargify API key. The password is x."
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:x"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("eShopOnWeb", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0"));
    }
}
