using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class BillingRegistration
{
    public static IServiceCollection AddSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Maxio").Get<MaxioSettings>() ?? new();
        foreach (var item in new[] { ("ApiKey", settings.ApiKey), ("Subdomain", settings.Subdomain), ("ProductFamilyHandle", settings.ProductFamilyHandle) })
            if (string.IsNullOrWhiteSpace(item.Item2))
                throw new InvalidOperationException($"Maxio:{item.Item1} is not configured.");
        if (settings.BaseUrl != null && (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http") || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTP(S) URL without credentials.");

        services.AddSingleton(settings);
        services.AddHttpClient("Maxio", c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        services.AddSingleton(sp =>
        {
            var options = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
                Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us,
                Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default() with
                {
                    MaxRetries = 1, Timeout = TimeSpan.FromSeconds(10),
                    HttpMethodsToRetry = new[] { HttpMethod.Get, HttpMethod.Head }
                },
                Logging = new MaxioAdvancedBilling.Core.Configuration.LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(), LogRequestBody = false,
                    LogRequestHeaders = false, LogResponseHeaders = false,
                    RedactedKeys = new[] { "sig", "signature", "access_token", "apikey", "api_key", "client_secret", "password",
                        "refresh_token", "code", "assertion", "client_assertion", "customer", "first_name", "last_name", "email", "reference" }
                }
            };
            options.Server.Production.Us.Site = settings.Subdomain;
            if (settings.BaseUrl != null) options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            return new MaxioAdvancedBilling.MaxioAdvancedBillingClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Maxio"), options);
        });
        services.AddScoped<SubscriptionBillingService>();
        services.AddScoped<ISubscriptionBilling>(sp => sp.GetRequiredService<SubscriptionBillingService>());
        services.AddHostedService<BillingReconciliationWorker>();
        return services;
    }
}
