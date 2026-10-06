using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioBillingServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing. The host refuses to start when the
    /// <c>Maxio</c> settings are missing or invalid.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        // A named client keeps Maxio's timeout and handler off the app's shared default HttpClient.
        services.AddHttpClient(HttpClientName)
            .ConfigureHttpClient((sp, http) =>
            {
                var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
                // Per-attempt backstop just above the SDK's own per-attempt timeout.
                http.Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds + 2);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton: recycle pooled connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are built once here, so a rotated API key takes effect on process restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient,
                CreateClientOptions(settings, sp.GetRequiredService<ILoggerFactory>()));
        });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            return new SubscriptionServiceSettings
            {
                RequestBudget = TimeSpan.FromSeconds(settings.RequestBudgetSeconds),
                SettleBudget = TimeSpan.FromSeconds(settings.SettleBudgetSeconds),
                ReferencePrefix = settings.ReferencePrefix
            };
        });
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ISubscriptionBillingGateway, MaxioBillingGateway>();
        services.AddScoped<ISubscriptionEnrollmentStore, EfSubscriptionEnrollmentStore>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        return services;
    }

    public static MaxioAdvancedBillingClientOptions CreateClientOptions(MaxioSettings settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!.Trim(), Password = "x" },
            Environment = settings.IsEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                // Only reads are ever resent; POST writes are settled by reference instead (see MaxioBillingGateway).
                HttpMethodsToRetry = [HttpMethod.Get],
                MaxRetries = settings.MaxReadRetries,
                Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds)
            },
            // Assigned explicitly so the SDK's log environment variable cannot switch body logging on:
            // customer requests carry e-mail addresses and names.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            options.Server.Production.Us.Site = settings.Subdomain.Trim();
            options.Server.Production.Eu.Site = settings.Subdomain.Trim();
        }
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            var baseUrl = settings.BaseUrl.Trim().TrimEnd('/');
            options.Server.Production.Us.BaseUrl = baseUrl;
            options.Server.Production.Eu.BaseUrl = baseUrl;
        }
        return options;
    }

    private sealed class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
    {
        public ValidateOptionsResult Validate(string? name, MaxioSettings options)
        {
            var errors = options.Validate();
            return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
        }
    }
}
