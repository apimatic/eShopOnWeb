using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPal;
using PayPal.Core.Authentication.OAuth2.ClientCredentials;
using PayPal.Core.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>The SDK gets its own named HttpClient, so its timeout and handlers stay off the shared default one.</summary>
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Bounds one attempt. The SDK's retry timeout is per attempt, not per call — the whole-call
    /// budget is a deadline token, applied in <see cref="PayPalPaymentGateway"/>.
    /// </summary>
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Registers PayPal payments: settings (validated at startup), the SDK client, the gateway and
    /// the application services built on it.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<PayPalSettings>()
            .Bind(configuration.GetSection(PayPalSettings.SectionName))
            .ValidateOnStart();

        // ValidateOnStart is the load-bearing half: without it, options validation is lazy and the
        // failure lands on the first request instead of on startup.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<PayPalSettings>, PayPalSettingsValidator>());

        services.AddHttpClient(HttpClientName, http =>
            {
                // Default is 100s, and it bounds one attempt, not the call.
                http.Timeout = AttemptTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton, so the factory's handler rotation never
                // applies to it. Recycling pooled connections is what keeps DNS from going stale.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalClientOptions
            {
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId!.Trim(),
                    ClientSecret = settings.ClientSecret!.Trim()
                },

                // This SDK version declares exactly one environment. The deployment is selected by
                // base URL below, never by this value.
                Environment = global::PayPal.Servers.ServerEnvironment.Production,

                Retry = RetryOptions.Default() with
                {
                    // Left as generated: GET/HEAD/PUT/OPTIONS only, so no POST or DELETE in this
                    // integration is ever resent by the SDK. That is what keeps a transport blip
                    // from authorizing, capturing or refunding twice.
                    MaxRetries = 2,
                    Timeout = AttemptTimeout
                },

                Logging = new LoggingOptions
                {
                    // Assigned explicitly so the SDK's PAYPALSERVERSDKCLIENT_LOG environment
                    // variable cannot switch body logging on from outside this code.
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),

                    // This integration posts card numbers. JSON request bodies are logged
                    // unredacted when this is on, so it stays off.
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            // When PayPal:BaseUrl is set it is used verbatim for every call — the OAuth token
            // request included, because the SDK resolves /v1/oauth2/token through this same server.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl!.Trim();
            }

            return new PayPalClient(httpClient, options);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();

        // One lock provider for the process: it serializes the payment operations on a single order.
        services.AddSingleton<OrderLockProvider>();

        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
