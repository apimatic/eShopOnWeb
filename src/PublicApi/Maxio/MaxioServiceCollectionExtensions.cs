using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the Maxio SDK client (singleton, options captured once at first resolution) and the billing services.
    /// The host refuses to start when a required Maxio setting is missing.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(60))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var s = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var environment = s.ResolveEnvironment()!;
            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = s.ApiKey!, Password = "x" },
                Environment = environment,
                // Per-attempt timeout; GET/PUT are retried at most twice, POST/DELETE are never resent by the SDK.
                Retry = RetryOptions.Default() with { MaxRetries = 2, Timeout = TimeSpan.FromSeconds(15) },
            };
            options.Logging = options.Logging with
            {
                LoggerFactory = sp.GetRequiredService<ILoggerFactory>()
            };

            if (environment == ServerEnvironment.Eu)
            {
                options.Server.Production.Eu.Site = s.Subdomain!.Trim();
                if (!string.IsNullOrWhiteSpace(s.BaseUrl)) options.Server.Production.Eu.BaseUrl = s.BaseUrl;
            }
            else
            {
                options.Server.Production.Us.Site = s.Subdomain!.Trim();
                if (!string.IsNullOrWhiteSpace(s.BaseUrl)) options.Server.Production.Us.BaseUrl = s.BaseUrl;
            }

            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(http, options);
        });

        services.AddScoped<PlanCatalog>();
        services.AddScoped<MaxioCustomers>();
        services.AddScoped<SubscriptionBilling>();
        return services;
    }
}

/// <summary>Fails host start-up naming the missing/invalid key (never its value).</summary>
internal sealed class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var problem = options.FirstProblem();
        return problem is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(problem + ". Set it with user-secrets or an environment variable (Maxio__<Key>).");
    }
}
