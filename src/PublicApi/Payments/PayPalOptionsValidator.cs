using System;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Fails startup when a credential is missing or blank, so a misconfiguration surfaces at boot rather
/// than as a 401 on the first PayPal call. Each part is checked separately — a blank part is not a
/// missing one — and the message names the config key without ever echoing a value.
/// </summary>
public sealed class PayPalOptionsValidator : IValidateOptions<PayPalOptions>
{
    public ValidateOptionsResult Validate(string? name, PayPalOptions options)
    {
        var failures = new System.Collections.Generic.List<string>();

        void RequireNonBlank(string? value, string key)
        {
            if (string.IsNullOrWhiteSpace(value))
                failures.Add($"{PayPalOptions.SectionName}:{key} is not configured. " +
                             "Set it via environment variable, user-secrets, or your secret store before starting the app.");
        }

        RequireNonBlank(options.ClientId, nameof(PayPalOptions.ClientId));
        RequireNonBlank(options.ClientSecret, nameof(PayPalOptions.ClientSecret));
        RequireNonBlank(options.Environment, nameof(PayPalOptions.Environment));
        RequireNonBlank(options.Currency, nameof(PayPalOptions.Currency));

        if (!string.IsNullOrWhiteSpace(options.Currency) && options.Currency.Trim().Length != 3)
            failures.Add($"{PayPalOptions.SectionName}:{nameof(PayPalOptions.Currency)} must be a 3-letter ISO-4217 code.");

        if (!string.IsNullOrWhiteSpace(options.BaseUrl) &&
            !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
            failures.Add($"{PayPalOptions.SectionName}:{nameof(PayPalOptions.BaseUrl)} must be a valid absolute URL when set.");

        if (options.RequestTimeoutSeconds <= 0)
            failures.Add($"{PayPalOptions.SectionName}:{nameof(PayPalOptions.RequestTimeoutSeconds)} must be greater than zero.");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
