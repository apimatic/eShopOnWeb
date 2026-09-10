using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Wires the PayPal payment integration: binds and fail-fast-validates the <c>PayPal:</c> settings,
/// constructs a single long-lived <see cref="PayPalServerSdkClient"/> over a named, pooled HttpClient,
/// and registers the processor and application service.
/// </summary>
public static class PaymentServiceRegistration
{
    private const string HttpClientName = "PayPalServerSdk";

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName));

        services.AddSingleton<IPaymentSettings>(sp => sp.GetRequiredService<IOptions<PayPalOptions>>().Value);

        // Read once at registration so the singleton captures a snapshot (rotation takes effect on restart).
        var settings = configuration.GetSection(PayPalOptions.SectionName).Get<PayPalOptions>() ?? new PayPalOptions();

        // Fail-fast: the host refuses to start if any required credential/setting is missing or blank.
        // Each part is checked (a blank part is not a missing one); the value is never echoed.
        RequireConfigured(settings.ClientId, $"{PayPalOptions.SectionName}:ClientId", "PAYPAL_CLIENT_ID");
        RequireConfigured(settings.ClientSecret, $"{PayPalOptions.SectionName}:ClientSecret", "PAYPAL_CLIENT_SECRET");
        RequireConfigured(settings.Environment, $"{PayPalOptions.SectionName}:Environment", "PAYPAL_ENVIRONMENT");
        RequireConfigured(settings.Currency, $"{PayPalOptions.SectionName}:Currency", "PAYPAL_CURRENCY");

        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(120))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5) // keep DNS fresh behind the singleton
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                // The SDK declares only Sandbox; all deployments target it. A different account is reached
                // by supplying different credentials (and optionally PayPal:BaseUrl), never a code change.
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                // Per-attempt timeout (a CancellationToken deadline in the processor bounds the whole call).
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) },
                // Assign LoggerFactory explicitly so the SDK's log env var cannot force body logging on;
                // request bodies (which carry card data) are never logged.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false
                }
            };

            // Optional override: used verbatim as the base for every call, including the token request.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl!;

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPaymentProcessor, PayPalPaymentProcessor>();
        services.AddScoped<IPaymentApplicationService, PaymentApplicationService>();

        return services;
    }

    private static void RequireConfigured(string? value, string configKey, string envVarName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"{configKey} is not configured. Set it via .NET user-secrets (from the {envVarName} " +
                "environment variable) or your secret store before starting the app.");
    }
}
