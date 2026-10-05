using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

/// <summary>
/// Bound from the <c>PayPal:</c> configuration section. Values come from configuration only (user-secrets in
/// development; environment / secret store elsewhere) — none are hard-coded.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";
    public const string SandboxEnvironment = "sandbox";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" — the only environment the SDK declares. Any other value requires <see cref="BaseUrl"/>.</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency every order is charged in.</summary>
    public string? Currency { get; set; }

    /// <summary>Optional. When set, used verbatim as the base address of every PayPal call, including the token request.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Total PayPal time one API request may spend, across every PayPal call it makes.</summary>
    public TimeSpan RequestBudget { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>Bound on a single HTTP attempt.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public bool IsSandbox => string.Equals(Environment?.Trim(), SandboxEnvironment, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Refuses to let the host start with incomplete PayPal settings. Messages name the missing key and never echo a value.
/// </summary>
public class PayPalOptionsValidator : IValidateOptions<PayPalOptions>
{
    public ValidateOptionsResult Validate(string? name, PayPalOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ClientId))
            failures.Add("PayPal:ClientId is not configured.");
        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            failures.Add("PayPal:ClientSecret is not configured.");
        if (string.IsNullOrWhiteSpace(options.Currency) || options.Currency.Trim().Length != 3)
            failures.Add("PayPal:Currency must be a three-letter ISO-4217 currency code.");
        if (string.IsNullOrWhiteSpace(options.Environment))
            failures.Add("PayPal:Environment is not configured (expected 'sandbox', or set PayPal:BaseUrl for another environment).");
        else if (!options.IsSandbox && string.IsNullOrWhiteSpace(options.BaseUrl))
            failures.Add("PayPal:Environment is not 'sandbox'; the PayPal SDK only declares the sandbox host, so PayPal:BaseUrl must be set for this environment.");
        if (!string.IsNullOrWhiteSpace(options.BaseUrl)
            && (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
            failures.Add("PayPal:BaseUrl must be an absolute http(s) URL.");
        if (options.RequestBudget <= TimeSpan.Zero || options.RequestBudget > TimeSpan.FromSeconds(30))
            failures.Add("PayPal:RequestBudget must be greater than zero and at most 30 seconds.");
        if (options.AttemptTimeout <= TimeSpan.Zero || options.AttemptTimeout > options.RequestBudget)
            failures.Add("PayPal:AttemptTimeout must be greater than zero and no longer than PayPal:RequestBudget.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
