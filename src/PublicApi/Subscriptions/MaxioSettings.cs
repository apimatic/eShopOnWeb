using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    public static void AddSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>().Bind(configuration.GetSection("Maxio"))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey), "Maxio:ApiKey is required.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Subdomain), "Maxio:Subdomain is required.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required.")
            .Validate(s => s.BaseUrl == null || (Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback)) &&
                string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)),
                "Maxio:BaseUrl must be an absolute HTTPS address (HTTP loopback is allowed for tests).")
            .ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MaxioSettings>>().Value);
        services.AddHttpClient("Maxio", client => client.Timeout = System.Threading.Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<MaxioSettings>();
            var options = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
                Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us,
                Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default() with
                {
                    MaxRetries = 1, Timeout = TimeSpan.FromSeconds(10), HttpMethodsToRetry = new[] { HttpMethod.Get }
                },
                Logging = new MaxioAdvancedBilling.Core.Configuration.LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false, LogRequestHeaders = false, LogResponseHeaders = false,
                    RedactedKeys = new[] { "api_key", "password", "email", "first_name", "last_name", "reference" }
                }
            };
            options.Server.Production.Us.Site = settings.Subdomain;
            if (settings.BaseUrl != null) options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            return new MaxioAdvancedBilling.MaxioAdvancedBillingClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Maxio"), options);
        });
        services.AddScoped<BillingClaimStore>();
        services.AddScoped<SubscriptionService>();
        services.AddHostedService<BillingReconciliationWorker>();
    }
}
