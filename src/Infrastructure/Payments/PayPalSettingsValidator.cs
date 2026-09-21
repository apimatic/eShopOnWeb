using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Fails host startup (via <c>ValidateOnStart</c>) when any required PayPal credential is missing or blank,
/// rather than discovering it as a 401 on the first call in production. Every part is checked separately —
/// a blank part is not a missing one — and the message names the config key without ever echoing a value.
/// </summary>
public sealed class PayPalSettingsValidator : IValidateOptions<PayPalSettings>
{
    public ValidateOptionsResult Validate(string? name, PayPalSettings options)
    {
        var failures = new List<string>();

        RequireNonBlank(options.ClientId, "PayPal:ClientId", failures);
        RequireNonBlank(options.ClientSecret, "PayPal:ClientSecret", failures);
        RequireNonBlank(options.Environment, "PayPal:Environment", failures);
        RequireNonBlank(options.Currency, "PayPal:Currency", failures);

        // The SDK declares only a Sandbox environment. Any other environment can only be reached by an
        // explicit BaseUrl override; without one, refuse to start rather than send traffic to the wrong host.
        if (!string.IsNullOrWhiteSpace(options.Environment)
            && !string.Equals(options.Environment, "sandbox", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            failures.Add(
                $"PayPal:Environment is '{options.Environment}', but the SDK supports only 'sandbox'. " +
                "Set PayPal:Environment=sandbox, or set PayPal:BaseUrl to target another host explicitly.");
        }

        if (options.RequestTimeoutSeconds <= 0)
        {
            failures.Add("PayPal:RequestTimeoutSeconds must be greater than zero.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void RequireNonBlank(string? value, string key, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add(
                $"{key} is not configured. Set it via environment variable, user-secrets, or your secret " +
                "store before starting the app.");
        }
    }
}
