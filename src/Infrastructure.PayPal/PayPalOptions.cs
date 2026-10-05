using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Bound from the <c>PayPal:</c> configuration section. Values come from user-secrets (development),
/// environment variables or a secret store — never from files in the repository.
/// </summary>
public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";
    public const string SandboxEnvironment = "sandbox";

    /// <summary><c>PayPal:ClientId</c> (from PAYPAL_CLIENT_ID).</summary>
    public string? ClientId { get; set; }

    /// <summary><c>PayPal:ClientSecret</c> (from PAYPAL_CLIENT_SECRET).</summary>
    public string? ClientSecret { get; set; }

    /// <summary><c>PayPal:Environment</c> (from PAYPAL_ENVIRONMENT), e.g. <c>sandbox</c>.</summary>
    public string? Environment { get; set; }

    /// <summary><c>PayPal:Currency</c> (from PAYPAL_CURRENCY), ISO-4217, e.g. <c>USD</c>.</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// <c>PayPal:BaseUrl</c> — optional. When set it is used verbatim as the base address of every
    /// PayPal call, the OAuth token request included, instead of the one derived from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsSandbox => string.Equals(Environment?.Trim(), SandboxEnvironment, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Startup validation: the host refuses to start rather than failing the first payment with a 401.</summary>
public sealed class PayPalOptionsValidator : IValidateOptions<PayPalOptions>
{
    public ValidateOptionsResult Validate(string? name, PayPalOptions options)
    {
        var failures = new List<string>();

        // Never echo a value (not even partially) — only the key that is missing.
        if (string.IsNullOrWhiteSpace(options.ClientId))
            failures.Add("PayPal:ClientId is not configured. Set it via user-secrets, the PayPal__ClientId / PAYPAL_CLIENT_ID environment variable, or your secret store.");
        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            failures.Add("PayPal:ClientSecret is not configured. Set it via user-secrets, the PayPal__ClientSecret / PAYPAL_CLIENT_SECRET environment variable, or your secret store.");
        if (string.IsNullOrWhiteSpace(options.Environment))
            failures.Add("PayPal:Environment is not configured (e.g. 'sandbox').");
        if (string.IsNullOrWhiteSpace(options.Currency) || options.Currency.Trim().Length != 3 || !options.Currency.Trim().All(char.IsAsciiLetter))
            failures.Add("PayPal:Currency must be a three-letter ISO-4217 code (e.g. USD).");

        var hasBaseUrl = !string.IsNullOrWhiteSpace(options.BaseUrl);
        if (hasBaseUrl && (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
            failures.Add("PayPal:BaseUrl must be an absolute http(s) URL when set.");

        // The SDK declares only the sandbox host; any other environment must say where to go.
        if (!string.IsNullOrWhiteSpace(options.Environment) && !options.IsSandbox && !hasBaseUrl)
            failures.Add($"PayPal:Environment is '{options.Environment}', for which no API host is built in; set PayPal:BaseUrl to that environment's API base address.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>
/// Time limits for PayPal calls (code defaults; overridable through DI, e.g. in tests). The request
/// budget bounds the total PayPal time of one API request, settlement re-reads included.
/// </summary>
public sealed class PayPalResilienceSettings
{
    /// <summary>Total PayPal time one API request may spend. Kept below the 30 s promise to callers.</summary>
    public TimeSpan RequestBudget { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>Part of the budget held back from a write so its outcome can still be settled by a re-read.</summary>
    public TimeSpan SettlementReserve { get; set; } = TimeSpan.FromSeconds(6);

    /// <summary>Per-attempt bound inside the SDK.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Backstop on the HttpClient (per attempt).</summary>
    public TimeSpan HttpClientTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Retries for reads (GET) only; writes are never resent by the SDK.</summary>
    public int MaxReadRetries { get; set; } = 2;
}
