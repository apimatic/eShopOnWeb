using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceRegistration
{
    /// <summary>
    /// Registers the PayPal SDK client (built once at registration and captured in the singleton, so a
    /// rotated secret needs a restart), plus the gateway and orchestration services. Credential binding and
    /// fail-fast validation are wired by the host (see PublicApi Program.cs) via
    /// <c>AddOptions&lt;PayPalSettings&gt;().Validate(...).ValidateOnStart()</c>.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.SectionName);
        var clientId = section["ClientId"] ?? string.Empty;
        var clientSecret = section["ClientSecret"] ?? string.Empty;
        var baseUrl = section["BaseUrl"];

        services.AddPayPalServerSdkClient(options =>
        {
            // The SDK declares only Sandbox; a live/other host is reached via the BaseUrl override below.
            options.Environment = ServerEnvironment.Sandbox;
            options.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            };

            // When BaseUrl is set, use it verbatim for every call — including the OAuth token request,
            // which resolves through this same server base URL.
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = baseUrl;
            }

            // Card data flows in request bodies — never log bodies. The DI extension assigns LoggerFactory
            // from the container, so the PAYPALSERVERSDKCLIENT_LOG env var cannot switch body logging on.
            options.Logging = options.Logging with
            {
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            };

            // Per-attempt timeout kept short; POST writes are never auto-resent by the SDK. The whole-call
            // budget is enforced by the CancellationToken the services pass.
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) };
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();

        return services;
    }
}
