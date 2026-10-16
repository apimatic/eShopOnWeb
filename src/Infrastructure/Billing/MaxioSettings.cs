using System;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioSettings
{
    [Required] public string ApiKey { get; set; } = "";
    [Required] public string Subdomain { get; set; } = "";
    [Required] public string ProductFamilyHandle { get; set; } = "";
    public string? BaseUrl { get; set; }
    public string Environment { get; set; } = "US";

    public string Scope => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        BaseUrl ?? $"{Environment.ToUpperInvariant()}:{Subdomain.ToLowerInvariant()}")));
}

public static class BillingRegistration
{
    public static IServiceCollection AddSubscriptionBilling(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<MaxioSettings>().Bind(config.GetSection("Maxio"))
            .Validate(x => !string.IsNullOrWhiteSpace(x.ApiKey), "Maxio:ApiKey is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.Subdomain) &&
                System.Text.RegularExpressions.Regex.IsMatch(x.Subdomain, @"\A[a-zA-Z0-9-]+\z"), "Maxio:Subdomain is required and must be a site name.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required.")
            .Validate(x => x.Environment is "US" or "EU", "Maxio:Environment must be US or EU.")
            .Validate(x => x.BaseUrl is null || (Uri.TryCreate(x.BaseUrl, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "https" || uri.Scheme == "http") && string.IsNullOrEmpty(uri.UserInfo) &&
                string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)), "Maxio:BaseUrl must be an absolute HTTP(S) address without credentials, query or fragment.")
            .ValidateOnStart();
        services.AddHttpClient("Maxio", client => client.Timeout = TimeSpan.FromSeconds(6))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AllowAutoRedirect = false
            });
        services.AddSingleton(sp =>
        {
            // Captured once; secret rotation takes effect on process restart.
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var options = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
            {
                Environment = settings.Environment == "EU" ? MaxioAdvancedBilling.Servers.ServerEnvironment.Eu : MaxioAdvancedBilling.Servers.ServerEnvironment.Us,
                BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
                Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(5),
                    HttpMethodsToRetry = new[] { HttpMethod.Get, HttpMethod.Head }
                },
                // Request URLs can contain personal references. Emit safe operation metrics in the gateway instead.
                Logging = new MaxioAdvancedBilling.Core.Configuration.LoggingOptions
                {
                    LoggerFactory = NullLoggerFactory.Instance,
                    LogRequestBody = false, LogRequestHeaders = false, LogResponseHeaders = false,
                    RedactedKeys = new[] { "sig", "signature", "access_token", "apikey", "api_key", "client_secret", "password", "refresh_token", "code", "assertion", "client_assertion", "first_name", "last_name", "email", "reference", "customer_reference" }
                }
            };
            if (settings.Environment == "EU")
            {
                options.Server.Production.Eu.Site = settings.Subdomain;
                if (settings.BaseUrl is not null) options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain;
                if (settings.BaseUrl is not null) options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }
            return new MaxioAdvancedBilling.MaxioAdvancedBillingClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Maxio"), options);
        });
        services.AddSingleton<ISubscriptionGateway, MaxioSubscriptionGateway>();
        services.AddScoped<IBillingClaimStore, BillingClaimStore>();
        services.AddScoped<SubscriptionService>();
        return services;
    }
}
