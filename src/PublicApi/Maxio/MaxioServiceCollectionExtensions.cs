using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public static ServerEnvironment ParseEnvironment(string? value)
    {
        if (string.Equals(value?.Trim(), "US", StringComparison.OrdinalIgnoreCase)) return ServerEnvironment.Us;
        if (string.Equals(value?.Trim(), "EU", StringComparison.OrdinalIgnoreCase)) return ServerEnvironment.Eu;
        throw new InvalidOperationException("Maxio:Environment must be 'US' or 'EU'.");
    }

    /// <summary>
    /// Registers the Maxio SDK client (a singleton built once from the settings: rotating a secret needs a restart)
    /// and the billing service. The host refuses to start when a setting is missing or blank.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.CONFIG_NAME))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey), "Maxio:ApiKey is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Subdomain), "Maxio:Subdomain is not configured.")
            .Validate(s => string.Equals(s.Environment?.Trim(), "US", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(s.Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase),
                "Maxio:Environment must be 'US' or 'EU'.")
            .ValidateOnStart();

        services.AddHttpClient();
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var environment = ParseEnvironment(settings.Environment);
            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
                Environment = environment,
            };
            options.Logging = options.Logging with { LoggerFactory = sp.GetRequiredService<ILoggerFactory>() };
            options.Server.Production.Us.Site = settings.Subdomain!;
            options.Server.Production.Eu.Site = settings.Subdomain!;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Optional override: used verbatim as the API's base address.
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
            }
            return new MaxioAdvancedBillingClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(), options);
        });

        services.AddScoped<IMaxioBillingService, MaxioBillingService>();
        return services;
    }
}
