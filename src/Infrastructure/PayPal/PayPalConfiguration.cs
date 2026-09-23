using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Wires the PayPal integration: binds <c>PayPal:*</c>, fails startup fast on a missing credential,
/// registers the SDK client (built once at registration, captured in the singleton), and the payment and
/// saved-card services.
/// </summary>
public static class PayPalConfiguration
{
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.CONFIG_NAME);
        services.Configure<PayPalSettings>(section);

        var settings = section.Get<PayPalSettings>() ?? new PayPalSettings();
        ValidateOrThrow(settings);

        // Options object is built here, once, and captured by the SDK singleton — a rotated secret takes
        // effect only after a process restart.
        services.AddPayPalServerSdkClient(options =>
        {
            options.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret,
            };
            options.Environment = ServerEnvironment.Sandbox;

            // Optional override: when PayPal:BaseUrl is set, it becomes the base for every call including the
            // OAuth token request (the token URL resolves through Server.Default.Sandbox.BaseUrl).
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl!.Trim();

            // LoggerFactory is filled from DI by AddPayPalServerSdkClient, which disables the
            // PAYPALSERVERSDKCLIENT_LOG environment variable; LogRequestBody stays off so card details in
            // request bodies are never logged.
        });

        services.AddScoped<IPayPalGateway>(sp => new PayPalGateway(
            sp.GetRequiredService<PayPalServerSdkClient>(),
            sp.GetRequiredService<ILogger<PayPalGateway>>(),
            settings.Currency));

        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        return services;
    }

    /// <summary>
    /// Refuses to boot when a credential is missing or blank (a deployment fault surfaced now, not as a
    /// 401 on the first request). Names the config key; never echoes a value. Checks every credential part.
    /// </summary>
    private static void ValidateOrThrow(PayPalSettings s)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(s.ClientId)) missing.Add("PayPal:ClientId");
        if (string.IsNullOrWhiteSpace(s.ClientSecret)) missing.Add("PayPal:ClientSecret");
        if (string.IsNullOrWhiteSpace(s.Environment)) missing.Add("PayPal:Environment");
        if (string.IsNullOrWhiteSpace(s.Currency)) missing.Add("PayPal:Currency");

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"PayPal configuration is incomplete. Set {string.Join(", ", missing)} via environment " +
                "variables, .NET user-secrets, or your secret store before starting the app.");

        if (!s.IsSandbox && string.IsNullOrWhiteSpace(s.BaseUrl))
            throw new InvalidOperationException(
                "PayPal:BaseUrl must be set when PayPal:Environment is not 'sandbox', so non-sandbox traffic " +
                "targets the correct base address.");
    }
}
