using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Validates PayPal settings at startup and refuses to boot when any required part is missing or
/// blank — so a misconfiguration surfaces as a clear startup error naming the config key, not as a
/// silent 401 on the first payment. Each part is checked separately (a blank part is not a missing
/// one) and the secret value is never echoed.
/// </summary>
public class PayPalSettingsValidator : IValidateOptions<PayPalSettings>
{
    public ValidateOptionsResult Validate(string? name, PayPalSettings options)
    {
        var failures = new System.Collections.Generic.List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
            failures.Add($"{PayPalSettings.SectionName}:ClientId is not configured.");
        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            failures.Add($"{PayPalSettings.SectionName}:ClientSecret is not configured.");
        if (string.IsNullOrWhiteSpace(options.Environment))
            failures.Add($"{PayPalSettings.SectionName}:Environment is not configured.");
        if (string.IsNullOrWhiteSpace(options.Currency))
            failures.Add($"{PayPalSettings.SectionName}:Currency is not configured.");

        // BaseUrl is optional, but if present it must be an absolute URL.
        if (!string.IsNullOrWhiteSpace(options.BaseUrl) &&
            !System.Uri.TryCreate(options.BaseUrl, System.UriKind.Absolute, out _))
            failures.Add($"{PayPalSettings.SectionName}:BaseUrl must be an absolute URL when set.");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
