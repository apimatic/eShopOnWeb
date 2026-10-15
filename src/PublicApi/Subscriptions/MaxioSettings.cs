using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSettings
{
    public string ApiKey { get; set; } = "";
    public string Subdomain { get; set; } = "";
    public string ProductFamilyHandle { get; set; } = "";
    public string? BaseUrl { get; set; }

    public static void Register(IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Maxio").Get<MaxioSettings>() ?? new();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is required.");
        if (!Regex.IsMatch(settings.Subdomain, @"\A[a-zA-Z0-9][a-zA-Z0-9-]*\z"))
            throw new InvalidOperationException("Maxio:Subdomain must be a site subdomain.");
        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is required.");
        if (settings.BaseUrl != null && (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)))
            throw new InvalidOperationException("Maxio:BaseUrl must be an HTTPS URL (HTTP allowed for loopback tests).");

        var options = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials
            {
                Username = settings.ApiKey, Password = "x"
            },
            Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Disabled() with { Timeout = TimeSpan.FromSeconds(10) },
            Logging = new MaxioAdvancedBilling.Core.Configuration.LoggingOptions
            {
                LoggerFactory = NullLoggerFactory.Instance, LogRequestBody = false,
                LogRequestHeaders = false, LogResponseHeaders = false
            }
        };
        options.Server.Production.Us.Site = settings.Subdomain;
        if (settings.BaseUrl != null) options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        services.AddSingleton(settings);
        services.AddHttpClient("Maxio", c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        services.AddSingleton(sp => new MaxioAdvancedBilling.MaxioAdvancedBillingClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Maxio"), options));
        services.AddScoped<ClaimStore>();
        services.AddScoped<MaxioGateway>();
        services.AddScoped<SubscriptionService>();
        services.AddHostedService<BillingReconciler>();
    }
}
