using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure;

/// <summary>
/// Wires the PayPal SDK client and <see cref="IPayPalGateway"/> from the "PayPal" configuration
/// section. Values must come from configuration (bound from PAYPAL_* env vars via user-secrets in
/// development) — never hard-coded, since the same build has to run against a different PayPal
/// account than the one used to develop it.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    public static IServiceCollection AddPayPalGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalCurrencyOptions>(configuration.GetSection("PayPal"));

        var clientId = configuration["PayPal:ClientId"]
            ?? throw new InvalidOperationException("PayPal:ClientId is not configured.");
        var clientSecret = configuration["PayPal:ClientSecret"]
            ?? throw new InvalidOperationException("PayPal:ClientSecret is not configured.");
        var environment = configuration["PayPal:Environment"]
            ?? throw new InvalidOperationException("PayPal:Environment is not configured.");
        var baseUrl = configuration["PayPal:BaseUrl"];

        if (!string.Equals(environment, "sandbox", StringComparison.OrdinalIgnoreCase))
        {
            // This SDK build exposes only ServerEnvironment.Sandbox (no Live/Production member) —
            // see paypal-plan.md §5 Blockers. PAYPAL_ENVIRONMENT for this task is "sandbox", so this
            // never trips here; it exists so a misconfigured value fails loudly instead of silently
            // running against the wrong (or a nonexistent) environment.
            throw new InvalidOperationException(
                $"PayPal:Environment '{environment}' is not supported — this PayPal SDK version only exposes ServerEnvironment.Sandbox.");
        }

        // AddPayPalServerSdkClient resolves the HttpClient from the default, unnamed IHttpClientFactory
        // client and registers PayPalServerSdkClient as a singleton holding it for the app's lifetime —
        // so give that default client a rotating connection pool rather than leaving DNS pinned forever.
        services.AddHttpClient(Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddPayPalServerSdkClient(options =>
        {
            options.Environment = ServerEnvironment.Sandbox;
            options.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            };
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20) };

            if (!string.IsNullOrEmpty(baseUrl))
            {
                // Verbatim override for every PayPal call, including the OAuth2 token request itself.
                options.Server.Default.Sandbox.BaseUrl = baseUrl;
            }
        });

        services.AddSingleton<IPayPalGateway, PayPalGateway>();

        return services;
    }
}
