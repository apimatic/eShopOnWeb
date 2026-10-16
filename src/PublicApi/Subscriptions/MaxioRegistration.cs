using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSettings
{
    public string ApiKey { get; set; } = "";
    public string Subdomain { get; set; } = "";
    public string ProductFamilyHandle { get; set; } = "";
    public string? BaseUrl { get; set; }
}

public static class MaxioRegistration
{
    public static void AddSubscriptionBilling(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var settings = configuration.GetSection("Maxio").Get<MaxioSettings>() ?? new();
        foreach (var entry in new[] { ("ApiKey", settings.ApiKey), ("Subdomain", settings.Subdomain), ("ProductFamilyHandle", settings.ProductFamilyHandle) })
            if (string.IsNullOrWhiteSpace(entry.Item2)) throw new InvalidOperationException("Maxio:" + entry.Item1 + " is required.");
        if (settings.BaseUrl != null && (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTP URL without credentials.");
        // Sandbox is a property of the supplied site; the SDK has no sandbox environment.
        var options = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
            Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default() with { MaxRetries = 1, Timeout = TimeSpan.FromSeconds(10), HttpMethodsToRetry = new[] { HttpMethod.Get } },
            // Explicit factory prevents environment-driven SDK tracing of personal data.
            Logging = new MaxioAdvancedBilling.Core.Configuration.LoggingOptions { LogRequestBody = false, LogRequestHeaders = false, LogResponseHeaders = false }
        };
        options.Server.Production.Us.Site = settings.Subdomain;
        if (settings.BaseUrl != null) options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        services.AddSingleton(settings);
        services.AddHttpClient("MaxioBilling", http => http.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        services.AddSingleton(sp =>
        {
            options.Logging = options.Logging with { LoggerFactory = sp.GetRequiredService<ILoggerFactory>() };
            return new MaxioAdvancedBilling.MaxioAdvancedBillingClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("MaxioBilling"), options);
        });
        services.AddScoped<SubscriptionService>();
    }
}
