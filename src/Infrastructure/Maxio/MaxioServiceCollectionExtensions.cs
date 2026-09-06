using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Wires the Maxio Advanced Billing integration into an application's service container.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Name of the dedicated <see cref="HttpClient"/> registration. A named client keeps this SDK's
    /// timeout, handlers and connection pooling off the shared default client — and off every other
    /// unnamed <c>CreateClient()</c> consumer in the app.
    /// </summary>
    public const string HttpClientName = "Maxio";

    /// <summary>Maxio's basic-auth convention: the API key is the username, the password is a literal "x".</summary>
    private const string ApiKeyPasswordPlaceholder = "x";

    /// <summary>
    /// Registers <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing, reading its
    /// settings from the <c>Maxio:</c> configuration section.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown at startup when a required setting is missing, so a misconfigured deployment fails fast
    /// instead of failing on the first shopper's subscribe.
    /// </exception>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioOptions.SectionName);
        var settings = section.Get<MaxioOptions>() ?? new MaxioOptions();
        Validate(settings);

        services.Configure<MaxioOptions>(section);
        services.AddMemoryCache();

        services.AddTransient<MaxioRequestLoggingHandler>();
        services.AddTransient<MaxioSingleSendHandler>();

        var httpClientBuilder = services.AddHttpClient(HttpClientName, http =>
        {
            // A backstop above the SDK's own per-attempt timeout: whichever fires first ends the call,
            // rather than letting a hung provider pin a request thread for the 100s default.
            http.Timeout = settings.AttemptTimeout + TimeSpan.FromSeconds(5);
        });

        if (settings.LogRequests)
        {
            httpClientBuilder.AddHttpMessageHandler<MaxioRequestLoggingHandler>();
        }

        httpClientBuilder
            .AddHttpMessageHandler<MaxioSingleSendHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton, so IHttpClientFactory's handler rotation never
                // reaches it. Without this, a DNS change would be cached for the process lifetime.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(options));
        });

        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    private static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioOptions settings)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = ApiKeyPasswordPlaceholder
            },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = settings.MaxRetries,
                Timeout = settings.AttemptTimeout
            }
        };

        // The base address is a template in which only the literal token "{site}" is substituted.
        // Setting Site covers the default template; a Maxio:BaseUrl override with no token is therefore
        // used verbatim, and one that does carry the token still resolves correctly.
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl.Trim();
        }

        options.Server.Production.Us.Site = settings.Subdomain.Trim();

        return options;
    }

    private static void Validate(MaxioOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set it with 'dotnet user-secrets set \"Maxio:ApiKey\" <value>' " +
                "or the Maxio__ApiKey environment variable.");
        }

        if (string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is not configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");
        }

        if (settings.MaxRetries < 1)
        {
            // Polly validates MaxRetryAttempts as >= 1 and would otherwise throw at client construction.
            throw new InvalidOperationException("Maxio:MaxRetries must be at least 1.");
        }
    }
}
