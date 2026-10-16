using System;
using System.Linq;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSettings
{
    public string ApiKey { get; set; } = "";
    public string Subdomain { get; set; } = "";
    public string ProductFamilyHandle { get; set; } = "";
    public string? BaseUrl { get; set; }

    public void Validate()
    {
        foreach (var pair in new[] { ("ApiKey", ApiKey), ("Subdomain", Subdomain), ("ProductFamilyHandle", ProductFamilyHandle) })
            if (string.IsNullOrWhiteSpace(pair.Item2))
                throw new InvalidOperationException($"Maxio:{pair.Item1} is required. Configure user-secrets or a deployment secret provider.");
        if (Subdomain.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new InvalidOperationException("Maxio:Subdomain must be a site subdomain.");
        if (BaseUrl != null && (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var url) ||
            (url.Scheme != "https" && url.Scheme != "http") || !string.IsNullOrEmpty(url.UserInfo) ||
            !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment) || BaseUrl.Contains('{')))
            throw new InvalidOperationException("Maxio:BaseUrl must be a literal absolute HTTP(S) API base address.");
    }
}

public static class MaxioRegistration
{
    public static IServiceCollection AddSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>().Bind(configuration.GetSection("Maxio"))
            .Validate(settings => { settings.Validate(); return true; }).ValidateOnStart();
        // Frozen once at host startup; configuration reloads/secret rotation require a restart.
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MaxioSettings>>().Value);
        services.AddHttpClient("MaxioBilling", client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        services.AddSingleton(provider =>
        {
            var settings = provider.GetRequiredService<MaxioSettings>();
            var options = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
            {
                Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us,
                BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials
                { Username = settings.ApiKey, Password = "x" },
                Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default() with
                {
                    HttpMethodsToRetry = new[] { HttpMethod.Get, HttpMethod.Head },
                    MaxRetries = 2,
                    Timeout = TimeSpan.FromSeconds(10)
                },
                Logging = new MaxioAdvancedBilling.Core.Configuration.LoggingOptions
                {
                    LoggerFactory = provider.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    RedactedKeys = new[] { "sig", "signature", "access_token", "apikey", "api_key", "client_secret", "password",
                        "refresh_token", "code", "assertion", "client_assertion", "first_name", "last_name", "email", "reference", "customer", "subscription" }
                }
            };
            options.Server.Production.Us.Site = settings.Subdomain;
            if (settings.BaseUrl != null) options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            return new MaxioAdvancedBilling.MaxioAdvancedBillingClient(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("MaxioBilling"), options);
        });
        services.AddScoped<MaxioGateway>();
        services.AddScoped<SubscriptionBillingService>();
        services.AddScoped<ISubscriptionBilling>(sp => sp.GetRequiredService<SubscriptionBillingService>());
        services.AddHostedService<BillingReconciliationWorker>();
        return services;
    }
}
