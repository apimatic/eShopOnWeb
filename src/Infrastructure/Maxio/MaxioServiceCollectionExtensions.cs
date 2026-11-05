using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HTTP_CLIENT_NAME = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.CONFIG_NAME))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey), "Maxio:ApiKey is not configured (set it via user-secrets or environment).")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Subdomain), "Maxio:Subdomain is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProductFamilyHandle), "Maxio:ProductFamilyHandle is not configured.")
            .Validate(s => string.IsNullOrWhiteSpace(s.BaseUrl)
                    || (Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)),
                "Maxio:BaseUrl must be an absolute http(s) URL when set.")
            .Validate(s => MaxioAdvancedBilling.Models.Enums.CollectionMethod.TryGetKnownValue(s.PaymentCollectionMethod, out _), "Maxio:PaymentCollectionMethod is not a known collection method.")
            .Validate(s => s.RequestBudget > TimeSpan.Zero, "Maxio:RequestBudget must be positive.")
            .ValidateOnStart();

        services.AddHttpClient(HTTP_CLIENT_NAME, c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are captured once here: a rotated API key needs a process restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) },
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false
                }
            };
            options.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HTTP_CLIENT_NAME);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<MaxioPlanCatalog>();
        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
        return services;
    }
}
