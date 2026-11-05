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
    // Per-attempt bound; the whole-call budget lives in MaxioGateway/SubscriptionService.
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        // Host refuses to start when a required Maxio setting is missing or blank.
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        services.AddHttpClient(HttpClientName, c => c.Timeout = AttemptTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are built once here and captured by the singleton: a rotated key needs a restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
                Retry = RetryOptions.Default() with { Timeout = AttemptTimeout, MaxRetries = 2 },
                // Explicit factory; request bodies (customer PII) are never logged.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false
                }
            };
            if (!string.IsNullOrWhiteSpace(settings.Subdomain))
                options.Server.Production.Us.Site = settings.Subdomain!;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!; // verbatim override

            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(http, options);
        });

        services.AddScoped<MaxioGateway>();
        services.AddScoped<SubscriptionService>();
        return services;
    }

    private sealed class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
    {
        public ValidateOptionsResult Validate(string? name, MaxioSettings options)
        {
            var failures = new System.Collections.Generic.List<string>();
            foreach (var r in options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options)))
                failures.Add(r.ErrorMessage ?? "invalid Maxio setting");
            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }
    }
}
