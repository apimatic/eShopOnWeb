using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Settings bound from the <c>PayPal:</c> configuration section. Values are supplied via environment
/// variables loaded into .NET user-secrets; none are hard-coded, so the same build runs against a
/// different PayPal account by changing configuration alone.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    /// <summary>From <c>PAYPAL_CLIENT_ID</c>.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>From <c>PAYPAL_CLIENT_SECRET</c>.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>From <c>PAYPAL_ENVIRONMENT</c> (this SDK only exposes <c>sandbox</c>).</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>ISO-4217 currency, from <c>PAYPAL_CURRENCY</c>.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base for every PayPal call —
    /// including the OAuth token request — instead of the environment default.
    /// </summary>
    public string? BaseUrl { get; set; }
}

/// <summary>
/// Fail-fast validation: the host refuses to start if any required credential/setting is missing or blank,
/// with a message that names the config key and never echoes a value. Every part is checked — a blank part
/// is not a missing one.
/// </summary>
public sealed class PayPalOptionsValidator : IValidateOptions<PayPalOptions>
{
    public ValidateOptionsResult Validate(string? name, PayPalOptions options)
    {
        var failures = new System.Collections.Generic.List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
            failures.Add("PayPal:ClientId is not configured (set PAYPAL_CLIENT_ID via user-secrets/environment).");
        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            failures.Add("PayPal:ClientSecret is not configured (set PAYPAL_CLIENT_SECRET via user-secrets/environment).");
        if (string.IsNullOrWhiteSpace(options.Environment))
            failures.Add("PayPal:Environment is not configured (set PAYPAL_ENVIRONMENT via user-secrets/environment).");
        if (string.IsNullOrWhiteSpace(options.Currency))
            failures.Add("PayPal:Currency is not configured (set PAYPAL_CURRENCY via user-secrets/environment).");

        // This SDK version declares only the Sandbox environment. A non-sandbox environment is only valid
        // when accompanied by an explicit PayPal:BaseUrl override, so test traffic can never silently reach
        // a live system.
        if (!string.IsNullOrWhiteSpace(options.Environment)
            && !string.Equals(options.Environment, "sandbox", System.StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            failures.Add($"PayPal:Environment '{options.Environment}' is not supported by this SDK (only 'sandbox'); " +
                         "set PayPal:BaseUrl to target another host explicitly.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
