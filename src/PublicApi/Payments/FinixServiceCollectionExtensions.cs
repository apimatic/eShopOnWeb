using System;
using System.Collections.Generic;
using System.Net.Http;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using FinixApiReference.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class FinixServiceCollectionExtensions
{
    private const string HttpClientName = "Finix";

    /// <summary>
    /// Maps the deployment's FINIX_* environment variables onto the exact <c>Finix:</c> keys the
    /// application binds. User-secrets remain a valid source; a supplied environment variable wins.
    /// </summary>
    public static void AddFinixEnvironmentConfiguration(this ConfigurationManager configuration)
    {
        var values = new Dictionary<string, string?>();

        AddIfPresent(values, "Finix:Username", Environment.GetEnvironmentVariable("FINIX_USERNAME"));
        AddIfPresent(values, "Finix:Password", Environment.GetEnvironmentVariable("FINIX_PASSWORD"));
        AddIfPresent(values, "Finix:MerchantId", Environment.GetEnvironmentVariable("FINIX_MERCHANT_ID"));
        AddIfPresent(values, "Finix:Environment", Environment.GetEnvironmentVariable("FINIX_ENVIRONMENT"));
        AddIfPresent(values, "Finix:Currency", Environment.GetEnvironmentVariable("FINIX_CURRENCY"));
        AddIfPresent(values, "Finix:BaseUrl", Environment.GetEnvironmentVariable("FINIX_BASEURL"));

        if (values.Count > 0)
        {
            configuration.AddInMemoryCollection(values);
        }
    }

    public static IServiceCollection AddFinixPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FinixOptions>()
            .Bind(configuration.GetSection(FinixOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Username), "Finix:Username is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Password), "Finix:Password is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.MerchantId), "Finix:MerchantId is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Currency), "Finix:Currency is not configured.")
            .Validate(o => string.Equals(o.Currency, "USD", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(o.Currency, "CAD", StringComparison.OrdinalIgnoreCase),
                "Finix:Currency must be USD or CAD.")
            .Validate(o => string.Equals(o.Environment, "sandbox", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(o.Environment, "live", StringComparison.OrdinalIgnoreCase),
                "Finix:Environment must be sandbox or live.")
            .Validate(o => !string.Equals(o.Environment, "live", StringComparison.OrdinalIgnoreCase)
                           || !string.IsNullOrWhiteSpace(o.BaseUrl),
                "Finix:BaseUrl is required when Finix:Environment is live.")
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var finix = sp.GetRequiredService<IOptions<FinixOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new FinixApiReferenceClientOptions
            {
                Environment = ServerEnvironment.Production,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = finix.Username,
                    Password = finix.Password
                },
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                },
                Retry = RetryOptions.Default() with
                {
                    HttpMethodsToRetry = [HttpMethod.Get],
                    Timeout = TimeSpan.FromSeconds(30)
                }
            };

            if (!string.IsNullOrWhiteSpace(finix.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = finix.BaseUrl!;
            }

            return new FinixApiReferenceClient(httpClient, options);
        });

        services.AddScoped<FinixPaymentService>();
        return services;
    }

    private static void AddIfPresent(IDictionary<string, string?> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value;
        }
    }
}
