using System;
using System.Linq;
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
        services.AddOptions<MaxioSettings>().Bind(configuration.GetSection("Maxio"))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey), "Maxio:ApiKey is required.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Subdomain), "Maxio:Subdomain is required.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required.")
            .Validate(s => s.BaseUrl == null || (Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https") && string.IsNullOrEmpty(u.UserInfo)), "Maxio:BaseUrl must be an absolute HTTP(S) address without credentials.")
            .ValidateOnStart();
        services.AddHttpClient("MaxioBilling", c => c.Timeout = TimeSpan.FromSeconds(6))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        // Configuration is captured once by the singleton. Restart the host for credential rotation.
        services.AddSingleton(sp => CreateClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("MaxioBilling"),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MaxioSettings>>().Value,
            sp.GetRequiredService<ILoggerFactory>(), configuration["MAXIO_ENVIRONMENT"]));
        services.AddScoped<ISubscriptionBilling, MaxioSubscriptionBilling>();
        services.AddScoped<MaxioSubscriptionBilling>();
        services.AddHostedService<BillingReconciler>();
        return services;
    }

    public static MaxioAdvancedBilling.MaxioAdvancedBillingClient CreateClient(HttpClient http, MaxioSettings settings, ILoggerFactory loggerFactory, string? region = null)
    {
        var options = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
            Environment = string.Equals(region, "EU", StringComparison.OrdinalIgnoreCase) ? MaxioAdvancedBilling.Servers.ServerEnvironment.Eu : MaxioAdvancedBilling.Servers.ServerEnvironment.Us,
            Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default() with
            {
                HttpMethodsToRetry = new[] { HttpMethod.Get, HttpMethod.Head }, MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(5), Delay = TimeSpan.FromMilliseconds(100), MaxJitter = TimeSpan.Zero
            },
            Logging = new MaxioAdvancedBilling.Core.Configuration.LoggingOptions
            {
                LoggerFactory = loggerFactory, LogRequestBody = false, LogRequestHeaders = false, LogResponseHeaders = false,
                RedactedKeys = new MaxioAdvancedBilling.Core.Configuration.LoggingOptions().RedactedKeys.Concat(new[] { "first_name", "last_name", "email", "reference" }).ToArray()
            }
        };
        if (options.Environment == MaxioAdvancedBilling.Servers.ServerEnvironment.Eu)
        {
            options.Server.Production.Eu.Site = settings.Subdomain;
            if (settings.BaseUrl != null) options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain;
            if (settings.BaseUrl != null) options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }
        return new MaxioAdvancedBilling.MaxioAdvancedBillingClient(http, options);
    }
}
