using System;
using System.Globalization;
using System.Net.Http;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using FinixApiReference.Core.Hooks;
using FinixApiReference.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class FinixServiceCollectionExtensions
{
    private const string HttpClientName = "FinixPayments";

    /// <summary>
    /// Binds <c>Finix:</c>, validates it (fail-fast), and registers the Finix SDK client plus the
    /// payment services. The SDK client and its options are built once and reused for the process.
    /// </summary>
    public static IServiceCollection AddFinixPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Finix").Get<FinixSettings>() ?? new FinixSettings();

        // The task supplies credentials as FINIX_* environment variables. They take effect when the
        // Finix: keys are not already present (e.g. from user-secrets); no value is written here.
        settings.Username = FirstNonBlank(settings.Username, Environment.GetEnvironmentVariable("FINIX_USERNAME"));
        settings.Password = FirstNonBlank(settings.Password, Environment.GetEnvironmentVariable("FINIX_PASSWORD"));
        settings.MerchantId = FirstNonBlank(settings.MerchantId, Environment.GetEnvironmentVariable("FINIX_MERCHANT_ID"));
        settings.Environment = FirstNonBlank(settings.Environment, Environment.GetEnvironmentVariable("FINIX_ENVIRONMENT"));
        settings.Currency = FirstNonBlank(settings.Currency, Environment.GetEnvironmentVariable("FINIX_CURRENCY"));
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            settings.BaseUrl = Environment.GetEnvironmentVariable("FINIX_BASE_URL");
        }

        settings.Validate();

        services.AddSingleton(settings);
        services.AddSingleton<PaymentGate>();
        services.AddSingleton<FinixPaymentGateway>();
        services.AddScoped<PaymentService>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var options = new FinixApiReferenceClientOptions
            {
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.Username,
                    Password = settings.Password
                },
                Environment = ServerEnvironment.Production,
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds)
                },
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                    LogRequestBody = false
                },
                // Finix's list endpoints accept date filters only as yyyy-MM-ddTHH:mm:ss.
                // The generated SDK serializes DateTimeOffset with an offset, so normalise the
                // date-range query values on the way out.
                Hooks = [SdkHook.OnRequest((request, _) => NormalizeDateRangeQuery(request))]
            };

            // Set the base address on the environment the client resolves. This honours
            // Finix:BaseUrl verbatim and otherwise selects sandbox/live from Finix:Environment.
            options.Server.Default.Production.BaseUrl = settings.ResolveBaseUrl();

            return new FinixApiReferenceClient(httpClient, options);
        });

        return services;
    }

    private static string FirstNonBlank(string? configured, string? fallback)
        => !string.IsNullOrWhiteSpace(configured) ? configured! : (fallback ?? string.Empty);

    private static void NormalizeDateRangeQuery(HttpRequestMessage request)
    {
        var uri = request.RequestUri;
        if (uri is null || string.IsNullOrEmpty(uri.Query)) return;

        var parts = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        var changed = false;

        for (var i = 0; i < parts.Length; i++)
        {
            var separator = parts[i].IndexOf('=');
            if (separator <= 0) continue;

            var key = Uri.UnescapeDataString(parts[i][..separator]);
            if (!key.EndsWith(".gte", StringComparison.OrdinalIgnoreCase) &&
                !key.EndsWith(".lte", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var raw = Uri.UnescapeDataString(parts[i][(separator + 1)..]);
            if (!DateTimeOffset.TryParse(
                    raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var value))
            {
                continue;
            }

            var formatted = value.ToUniversalTime()
                .ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            parts[i] = $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(formatted)}";
            changed = true;
        }

        if (changed)
        {
            request.RequestUri = new UriBuilder(uri) { Query = string.Join("&", parts) }.Uri;
        }
    }
}
