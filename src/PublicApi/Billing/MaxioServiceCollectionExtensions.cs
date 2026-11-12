using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioServiceCollectionExtensions
{
    private const string HTTP_CLIENT_NAME = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.CONFIG_NAME))
            .ValidateOnStart();

        services.AddHttpClient(HTTP_CLIENT_NAME, c => c.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HTTP_CLIENT_NAME);
            return new MaxioAdvancedBillingClient(httpClient, BuildOptions(settings));
        });

        services.AddSingleton<SubscribeClaims>();
        services.AddScoped<BillingService>();
        return services;
    }

    public static MaxioAdvancedBillingClientOptions BuildOptions(MaxioSettings settings)
    {
        var isEu = string.Equals(settings.Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);
        var options = new MaxioAdvancedBillingClientOptions
        {
            // The API key is the user name; the password is the literal "x" (SDK map, Servers & auth).
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!.Trim(), Password = "x" },
            Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
            Retry = RetryOptions.Default() with { MaxRetries = 2, Timeout = TimeSpan.FromSeconds(15) },
            // Request bodies carry customer e-mail addresses: never let the SDK log them.
            Logging = new LoggingOptions { LoggerFactory = NullLoggerFactory.Instance, LogRequestBody = false },
        };

        var subdomain = settings.Subdomain!.Trim();
        options.Server.Production.Us.Site = subdomain;
        options.Server.Production.Eu.Site = subdomain;

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Used verbatim as the API's base address for the selected environment.
            if (isEu)
            {
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
            }
            else
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }
        }
        return options;
    }
}
