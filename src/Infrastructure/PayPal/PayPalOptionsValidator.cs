using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Fails host startup (via <c>ValidateOnStart</c>) when a required PayPal setting is missing or blank,
/// rather than discovering it as a 401 on the first live call. Every credential part is checked
/// separately — a blank part is not a missing one. Never echoes a secret value.
/// </summary>
public class PayPalOptionsValidator : IValidateOptions<PayPalOptions>
{
    public ValidateOptionsResult Validate(string? name, PayPalOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
            failures.Add("PayPal:ClientId is not configured (set env PAYPAL_CLIENT_ID / user-secret).");

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            failures.Add("PayPal:ClientSecret is not configured (set env PAYPAL_CLIENT_SECRET / user-secret).");

        if (string.IsNullOrWhiteSpace(options.Currency))
            failures.Add("PayPal:Currency is not configured (set env PAYPAL_CURRENCY / user-secret).");
        else if (options.Currency.Trim().Length != 3)
            failures.Add("PayPal:Currency must be a three-letter ISO-4217 currency code.");

        // The SDK only ships a Sandbox environment. A non-sandbox environment is only usable with an
        // explicit BaseUrl override; otherwise refuse to boot rather than silently hit sandbox.
        var env = options.Environment?.Trim();
        var hasBaseUrl = !string.IsNullOrWhiteSpace(options.BaseUrl);
        if (!hasBaseUrl && !string.IsNullOrWhiteSpace(env) &&
            !env.Equals("sandbox", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"PayPal:Environment '{env}' is not supported without a PayPal:BaseUrl override " +
                "(the SDK provides only a Sandbox environment).");
        }

        if (hasBaseUrl && !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
            failures.Add("PayPal:BaseUrl must be an absolute URL when set.");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
