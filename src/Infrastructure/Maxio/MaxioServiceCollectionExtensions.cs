using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the Maxio client (singleton, built once at first use from the validated settings) and the
    /// subscription service. The host refuses to start when the Maxio settings are missing or blank.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();

        // Surface the precise (value-free) reason rather than the generic message.
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        services.AddHttpClient(HttpClientName)
            .ConfigureHttpClient((sp, c) => c.Timeout = sp.GetRequiredService<IOptions<MaxioSettings>>().Value.AttemptTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
                Retry = RetryOptions.Default() with { Timeout = settings.AttemptTimeout },
                Logging = new LoggingOptions
                {
                    // Explicit factory: the MAXIO_ADVANCED_BILLING_LOG env var can never force body logging on.
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false
                }
            };
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            else
                options.Server.Production.Us.Site = settings.Subdomain!;

            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(http, options);
        });

        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();
        return services;
    }

    private sealed class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
    {
        public ValidateOptionsResult Validate(string? name, MaxioSettings options)
        {
            var error = MaxioSettings.Validate(options);
            return error is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(error);
        }
    }
}
