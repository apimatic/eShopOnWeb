using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioOptions
{
    public string ApiKey { get; set; } = "";
    public string Subdomain { get; set; } = "";
    public string ProductFamilyHandle { get; set; } = "";
    public string? BaseUrl { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey)) throw new InvalidOperationException("Maxio:ApiKey is required.");
        if (string.IsNullOrWhiteSpace(Subdomain) || !Regex.IsMatch(Subdomain, "^[a-zA-Z0-9-]+$"))
            throw new InvalidOperationException("Maxio:Subdomain must be a site subdomain.");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle)) throw new InvalidOperationException("Maxio:ProductFamilyHandle is required.");
        if (BaseUrl != null && (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp) || !string.IsNullOrEmpty(url.UserInfo)
            || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment)))
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTP(S) API base address.");
    }
}

public static class BillingRegistration
{
    public static IServiceCollection AddSubscriptionBilling(this IServiceCollection services, IConfiguration config)
    {
        var settings = config.GetSection("Maxio").Get<MaxioOptions>() ?? new MaxioOptions();
        settings.Validate(); // Registration-time validation: even an unused integration must be configured.
        var region = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT") ?? "US";
        if (!region.Equals("US", StringComparison.OrdinalIgnoreCase) && !region.Equals("EU", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MAXIO_ENVIRONMENT must select US or EU hosting; sandbox is selected by its site.");
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = region.Equals("EU", StringComparison.OrdinalIgnoreCase) ? ServerEnvironment.Eu : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
            Retry = RetryOptions.Default() with { MaxRetries = 1, Timeout = TimeSpan.FromSeconds(8), HttpMethodsToRetry = new[] { HttpMethod.Get } }
        };
        if (options.Environment == ServerEnvironment.Eu)
        {
            options.Server.Production.Eu.Site = settings.Subdomain;
            if (settings.BaseUrl != null) options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain;
            if (settings.BaseUrl != null) options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }
        services.AddSingleton(settings);
        services.AddHttpClient("Maxio", http => http.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), AllowAutoRedirect = false });
        services.AddSingleton(sp =>
        {
            options.Logging = new LoggingOptions
            {
                LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                LogRequestBody = false, LogRequestHeaders = false, LogResponseHeaders = false,
                RedactedHeaders = new[] { "Authorization" },
                RedactedKeys = new[] { "api_key", "password", "first_name", "last_name", "email", "reference", "customer", "subscription" }
            };
            return new MaxioAdvancedBillingClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Maxio"), options);
        });
        services.AddScoped<MaxioGateway>();
        services.AddScoped<BillingClaimStore>();
        services.AddScoped<ISubscriptionBilling, SubscriptionBilling>();
        return services;
    }
}
