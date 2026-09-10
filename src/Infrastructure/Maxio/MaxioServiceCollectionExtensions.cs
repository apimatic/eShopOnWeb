using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing client and the subscription-billing service.
    /// Fails fast at startup (throws) when a required credential is missing or blank, so a
    /// misconfiguration surfaces as a boot failure rather than a 401 on the first shopper's request.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new MaxioOptions();
        configuration.GetSection(MaxioOptions.SectionName).Bind(options);

        // Fail-fast: every required part is checked independently — a blank part is not a missing one.
        RequireConfigured(options.ApiKey, $"{MaxioOptions.SectionName}:{nameof(MaxioOptions.ApiKey)}");
        RequireConfigured(options.Subdomain, $"{MaxioOptions.SectionName}:{nameof(MaxioOptions.Subdomain)}");
        RequireConfigured(options.ProductFamilyHandle, $"{MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)}");

        services.AddSingleton(Options.Create(options));

        services.AddMaxioAdvancedBillingClient(clientOptions =>
        {
            // Basic auth: the API key is the username, the password is the literal "x".
            clientOptions.BasicAuth = new BasicAuthCredentials
            {
                Username = options.ApiKey!,
                Password = "x"
            };

            clientOptions.Environment = ServerEnvironment.Us;

            // Base address: use the explicit override verbatim when present, otherwise substitute the
            // configured site subdomain into the default https://{site}.chargify.com template.
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl!;
            }
            else
            {
                clientOptions.Server.Production.Us.Site = options.Subdomain!;
            }

            // The SDK's Timeout is per-attempt; a whole-call budget is enforced by a CancellationToken
            // in MaxioSubscriptionBillingService. Keep the default retry policy: POST is not resent by
            // the SDK (HttpMethodsToRetry excludes POST), so create-customer/create-subscription writes
            // are never duplicated by a retry.
            clientOptions.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20) };
        });

        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    private static void RequireConfigured(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{key} is not configured. Set it via user-secrets or an environment variable before starting the app.");
        }
    }
}
