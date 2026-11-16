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

namespace Microsoft.eShopWeb.PublicApi.Finix;

public static class FinixServiceCollectionExtensions
{
    private const string HttpClientName = "FinixApi";

    /// <summary>
    /// Copies the Finix environment variables into the <c>Finix:</c> configuration section so
    /// the same build can be pointed at another account without editing any file. Values
    /// already supplied by user-secrets or another provider are only overridden when the
    /// environment variable is present and non-blank.
    /// </summary>
    public static void AddFinixEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>();

        Add(values, "Finix:Username", Environment.GetEnvironmentVariable("FINIX_USERNAME"));
        Add(values, "Finix:Password", Environment.GetEnvironmentVariable("FINIX_PASSWORD"));
        Add(values, "Finix:MerchantId", Environment.GetEnvironmentVariable("FINIX_MERCHANT_ID"));
        Add(values, "Finix:Environment", Environment.GetEnvironmentVariable("FINIX_ENVIRONMENT"));
        Add(values, "Finix:Currency", Environment.GetEnvironmentVariable("FINIX_CURRENCY"));

        if (values.Count > 0)
        {
            builder.AddInMemoryCollection(values);
        }
    }

    public static IServiceCollection AddFinixPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FinixOptions>()
            .Bind(configuration.GetSection(FinixOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FinixOptions>, FinixOptionsValidator>();

        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds one attempt; the SDK's own Retry.Timeout is set below as well.
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<FinixOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var options = new FinixApiReferenceClientOptions
            {
                Environment = ServerEnvironment.Production,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.Username,
                    Password = settings.Password
                },
                Retry = RetryOptions.Default() with
                {
                    // Never resend a money-moving write; reads may retry.
                    HttpMethodsToRetry = [HttpMethod.Get],
                    MaxRetries = 2,
                    Delay = TimeSpan.FromMilliseconds(500),
                    UseExponentialBackoff = true,
                    Timeout = TimeSpan.FromSeconds(10)
                },
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new FinixApiReferenceClient(httpClient, options);
        });

        services.AddScoped<FinixPaymentService>();
        return services;
    }

    private static void Add(IDictionary<string, string?> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value;
        }
    }
}
