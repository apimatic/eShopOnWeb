using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Registers Maxio Advanced Billing: binds <see cref="MaxioSettings"/> from the <c>Maxio:</c> section,
/// constructs a single long-lived <see cref="MaxioAdvancedBillingClient"/> over a pooled
/// <see cref="System.Net.Http.HttpClient"/>, and exposes it through <see cref="ISubscriptionBillingService"/>.
/// </summary>
public static class MaxioBillingServiceExtensions
{
    private const string HttpClientName = "maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        // The SDK client is long-lived (constructed once, below). A pooled connection lifetime lets it
        // pick up DNS changes instead of pinning a resolved address for the process lifetime.
        var httpBuilder = services.AddHttpClient(HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Optional wire logging for diagnosis (Maxio:DebugWireLogging=true). Off by default.
        if (bool.TryParse(configuration[$"{MaxioSettings.SectionName}:DebugWireLogging"], out var wireLog) && wireLog)
        {
            services.AddTransient<MaxioWireLoggingHandler>();
            httpBuilder.AddHttpMessageHandler<MaxioWireLoggingHandler>();
        }

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildOptions(settings));
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();
        return services;
    }

    private static MaxioAdvancedBillingClientOptions BuildOptions(MaxioSettings settings)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            // Maxio HTTP Basic auth: the site API key is the username, the password is the literal "x".
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" }
        };

        var isEu = string.Equals(settings.Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);
        if (isEu)
        {
            options.Environment = ServerEnvironment.Eu;
            options.Server.Production.Eu.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
        }
        else
        {
            options.Environment = ServerEnvironment.Us;
            options.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }

        return options;
    }
}
