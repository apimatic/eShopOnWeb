using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceExtensions
{
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Registers the PayPal integration: options (fail-fast at startup), the long-lived SDK client over a
    /// named, pooled <see cref="HttpClient"/>, the gateway, and the payment/vault services.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName))
            .ValidateOnStart();

        // Fail-fast validation: every credential part checked separately (blank ≠ missing), and the
        // environment must be one the SDK actually supports so test traffic can never reach a live host.
        services.AddSingleton<IValidateOptions<PayPalOptions>, PayPalOptionsValidator>();

        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Keeps DNS fresh behind the long-lived (singleton) SDK client.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        // Long-lived client — its OAuth token cache lives on the auth scheme, so it must be reused.
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var clientOptions = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = options.ClientId,
                    ClientSecret = options.ClientSecret,
                },
                // LoggerFactory set explicitly so the PAYPALSERVERSDKCLIENT_LOG env var cannot switch on
                // (unredacted) request-body logging from outside the code; body logging stays OFF (cards).
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                },
            };

            // Optional override: when set, use it verbatim as the base address for EVERY call, including
            // the OAuth token request (which resolves through this same server base URL).
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                clientOptions.Server.Default.Sandbox.BaseUrl = options.BaseUrl!;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new PayPalServerSdkClient(httpClient, clientOptions);
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();

        return services;
    }

    /// <summary>
    /// Fails startup when any PayPal setting is missing/blank, or the environment is unsupported. Never
    /// echoes a secret value — only names the offending key.
    /// </summary>
    private sealed class PayPalOptionsValidator : IValidateOptions<PayPalOptions>
    {
        public ValidateOptionsResult Validate(string? name, PayPalOptions options)
        {
            var failures = new System.Collections.Generic.List<string>();

            if (string.IsNullOrWhiteSpace(options.ClientId))
            {
                failures.Add("PayPal:ClientId is not configured.");
            }

            if (string.IsNullOrWhiteSpace(options.ClientSecret))
            {
                failures.Add("PayPal:ClientSecret is not configured.");
            }

            if (string.IsNullOrWhiteSpace(options.Environment))
            {
                failures.Add("PayPal:Environment is not configured.");
            }
            else if (!string.Equals(options.Environment, "Sandbox", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"PayPal:Environment '{options.Environment}' is not supported by this SDK build. Only 'Sandbox' is available.");
            }

            if (string.IsNullOrWhiteSpace(options.Currency))
            {
                failures.Add("PayPal:Currency is not configured.");
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }
    }
}
