using System;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public sealed class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public Uri GetBaseUri()
    {
        var value = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl
            : Environment.Equals("live", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.paypal.com"
                : Environment.Equals("sandbox", StringComparison.OrdinalIgnoreCase)
                    ? "https://api-m.sandbox.paypal.com"
                    : throw new OptionsValidationException(SectionName, typeof(PayPalSettings),
                        new[] { "PayPal:Environment must be either 'sandbox' or 'live'." });

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http"))
        {
            throw new OptionsValidationException(SectionName, typeof(PayPalSettings),
                new[] { "PayPal:BaseUrl must be an absolute HTTP or HTTPS URL." });
        }
        return uri;
    }
}

public sealed class PayPalSettingsValidator : IValidateOptions<PayPalSettings>
{
    public ValidateOptionsResult Validate(string? name, PayPalSettings settings)
    {
        var failures = new System.Collections.Generic.List<string>();
        if (string.IsNullOrWhiteSpace(settings.ClientId)) failures.Add("PayPal:ClientId is required.");
        if (string.IsNullOrWhiteSpace(settings.ClientSecret)) failures.Add("PayPal:ClientSecret is required.");
        if (settings.Environment is null ||
            (!settings.Environment.Equals("sandbox", StringComparison.OrdinalIgnoreCase) &&
             !settings.Environment.Equals("live", StringComparison.OrdinalIgnoreCase)))
            failures.Add("PayPal:Environment must be either 'sandbox' or 'live'.");
        if (string.IsNullOrWhiteSpace(settings.Currency) || settings.Currency.Length != 3 ||
            !System.Linq.Enumerable.All(settings.Currency, char.IsAsciiLetter))
            failures.Add("PayPal:Currency must be a three-letter currency code.");

        try { _ = settings.GetBaseUri(); }
        catch (OptionsValidationException ex) { failures.AddRange(ex.Failures); }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
