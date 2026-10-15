using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSettings
{
    public string ApiKey { get; set; } = "";
    public string Subdomain { get; set; } = "";
    public string ProductFamilyHandle { get; set; } = "";
    public string? BaseUrl { get; set; }
    // This also namespaces local claims when changing the configured billing site.
    public string Site => BaseUrl ?? Subdomain;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey)) throw new InvalidOperationException("Maxio:ApiKey is required.");
        if (string.IsNullOrWhiteSpace(Subdomain)) throw new InvalidOperationException("Maxio:Subdomain is required.");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle)) throw new InvalidOperationException("Maxio:ProductFamilyHandle is required.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(Subdomain, "^[a-zA-Z0-9][a-zA-Z0-9-]*$"))
            throw new InvalidOperationException("Maxio:Subdomain must be a site subdomain.");
        if (BaseUrl != null && (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || BaseUrl.Contains('{')))
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTPS address (HTTP is allowed on loopback) without credentials, query, fragment, or placeholders.");
    }
}

public static class SubscriptionRegistration
{
    public static IServiceCollection AddSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Maxio").Get<MaxioSettings>() ?? new MaxioSettings();
        settings.Validate();
        services.AddSingleton(settings);
        services.AddHttpClient("Maxio", client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AllowAutoRedirect = false
            });
        services.AddSingleton(provider =>
        {
            var options = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials
                {
                    Username = settings.ApiKey, Password = "x"
                },
                Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default() with
                {
                    MaxRetries = 0, Timeout = TimeSpan.FromSeconds(10)
                },
                Logging = new MaxioAdvancedBilling.Core.Configuration.LoggingOptions
                {
                    LoggerFactory = provider.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false, LogRequestHeaders = false, LogResponseHeaders = false
                }
            };
            options.Server.Production.Us.Site = settings.Subdomain;
            if (settings.BaseUrl != null) options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            return new MaxioAdvancedBilling.MaxioAdvancedBillingClient(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("Maxio"), options);
        });
        services.AddScoped<BillingClaimStore>();
        services.AddScoped<SubscriptionService>();
        services.AddHostedService<BillingReconciliationWorker>();
        return services;
    }
}
