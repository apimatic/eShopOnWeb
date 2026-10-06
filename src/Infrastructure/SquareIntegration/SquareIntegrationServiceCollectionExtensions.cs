using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

public static class SquareIntegrationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Square integration. The host refuses to start when a required <c>Square:</c> setting is missing.
    /// </summary>
    public static IServiceCollection AddSquareIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SquareSettings>()
            .Bind(configuration.GetSection(SquareSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SquareSettings>, SquareSettingsValidator>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddDataProtection();

        services.AddHttpClient(SquareClientFactory.HttpClientName, client => client.Timeout = SquareTimeouts.HttpClientTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The merchant client is long-lived; recycle pooled connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton<SquareClientFactory>();
        services.AddSingleton<SquareTokenProtector>();
        services.AddSingleton<SquareAccessTokenProvider>();
        services.AddSingleton<SquareClientProvider>();
        services.AddSingleton<SquareMerchantContextProvider>();
        services.AddSingleton<SquareCatalogWriteGate>();
        services.AddSingleton<SquareGiftMessageStore>();

        services.AddScoped<SquareOAuthService>();
        services.AddScoped<SquareCatalogSync>();
        services.AddScoped<SquareCatalogPhotoService>();
        services.AddScoped<SquareOrderSync>();
        services.AddScoped<SquareOrderService>();
        return services;
    }

    /// <summary>
    /// Lets the <c>SQUARE_*</c> environment variables supply the <c>Square:</c> keys. Added with the lowest
    /// precedence, so user-secrets, appsettings or <c>Square__*</c> variables win when they set the same key.
    /// </summary>
    public static IConfigurationBuilder AddSquareEnvironmentVariables(this IConfigurationBuilder configuration)
    {
        var map = new Dictionary<string, string>
        {
            ["SQUARE_ENVIRONMENT"] = "Square:Environment",
            ["SQUARE_APPLICATION_ID"] = "Square:ApplicationId",
            ["SQUARE_APPLICATION_SECRET"] = "Square:ApplicationSecret",
            ["SQUARE_REDIRECT_URI"] = "Square:RedirectUri",
            ["SQUARE_ACCESS_TOKEN"] = "Square:AccessToken",
        };

        var values = new Dictionary<string, string?>();
        foreach (var (variable, key) in map)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value)) values[key] = value;
        }

        configuration.Sources.Insert(0, new MemoryConfigurationSource { InitialData = values });
        return configuration;
    }
}
