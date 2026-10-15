using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Name of the <see cref="HttpClient"/> dedicated to Maxio (kept off the shared default client).</summary>
    public const string HttpClientName = "Maxio";

    /// <summary>Maxio's documented HTTP Basic password; the API key is the user name.</summary>
    private const string BasicAuthPassword = "x";

    /// <summary>
    /// Registers the Maxio Advanced Billing client and <see cref="ISubscriptionBillingGateway"/>.
    /// Settings are validated when the host starts: a missing credential stops the app from booting.
    /// The client is built once, so a rotated API key takes effect on the next process restart.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        services.AddHttpClient(HttpClientName, (sp, httpClient) =>
            {
                var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
                // Backstop per attempt; the SDK's own per-attempt timeout fires first.
                httpClient.Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds + 5);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton; recycle pooled connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(settings, sp.GetRequiredService<ILoggerFactory>(),
                sp.GetService<TimeProvider>() ?? TimeProvider.System));
        });

        services.AddSingleton<ISubscriptionBillingGateway, MaxioSubscriptionBillingGateway>();
        return services;
    }

    public static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioSettings settings, ILoggerFactory loggerFactory, TimeProvider timeProvider)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = settings.IsEuEnvironment ? ServerEnvironment.Eu : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = BasicAuthPassword },
            // GET/HEAD/PUT/OPTIONS only (SDK default): the POSTs in scope are never resent.
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 2,
                Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds)
            },
            // Set explicitly so the SDK's environment-variable logger can never switch on body logging
            // (customer requests carry e-mail addresses).
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestHeaders = false,
                LogResponseHeaders = false,
                LogRequestBody = false
            },
            TimeProvider = timeProvider
        };

        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? null : settings.BaseUrl.Trim().TrimEnd('/');
        if (settings.IsEuEnvironment)
        {
            if (!string.IsNullOrWhiteSpace(settings.Subdomain)) options.Server.Production.Eu.Site = settings.Subdomain.Trim();
            if (baseUrl is not null) options.Server.Production.Eu.BaseUrl = baseUrl;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(settings.Subdomain)) options.Server.Production.Us.Site = settings.Subdomain.Trim();
            if (baseUrl is not null) options.Server.Production.Us.BaseUrl = baseUrl;
        }
        return options;
    }

    /// <summary>Reports each missing/invalid key by name (never by value) when the host starts.</summary>
    private sealed class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
    {
        public ValidateOptionsResult Validate(string? name, MaxioSettings options)
        {
            var problems = options.Validate();
            return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
        }
    }
}
