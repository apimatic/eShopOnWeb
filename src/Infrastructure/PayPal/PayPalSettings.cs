using System;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal configuration, bound from the <c>PayPal</c> section. Nothing here has a baked-in value:
/// the same build has to run against a different PayPal account by configuration alone.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    /// <summary>REST client id of the merchant account. Configuration key <c>PayPal:ClientId</c>.</summary>
    public string? ClientId { get; set; }

    /// <summary>REST client secret. Configuration key <c>PayPal:ClientSecret</c>.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Which PayPal environment to talk to — <c>sandbox</c>. Configuration key
    /// <c>PayPal:Environment</c>. Any other value requires <see cref="BaseUrl"/> to be set as well,
    /// because this SDK version declares a base URL for the sandbox only.
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>Three-letter ISO-4217 currency for every amount. Configuration key <c>PayPal:Currency</c>.</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim for every PayPal
    /// call, the OAuth token request included. Configuration key <c>PayPal:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public const string SandboxEnvironment = "sandbox";

    public bool IsSandbox =>
        string.Equals(Environment?.Trim(), SandboxEnvironment, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Refuses to let the host start on incomplete PayPal configuration. A blank credential discovered
/// as a 401 on the first real payment looks like a provider outage; discovered at startup it looks
/// like what it is — an unset configuration value.
/// </summary>
public class PayPalSettingsValidator : IValidateOptions<PayPalSettings>
{
    public ValidateOptionsResult Validate(string? name, PayPalSettings options)
    {
        var failures = new System.Collections.Generic.List<string>();

        // Each part is checked on its own: a blank half of a credential pair is not "partly
        // configured", it is misconfigured.
        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("PayPal:ClientId is not configured. Set it via user-secrets, an environment " +
                         "variable, or your secret store before starting the app.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add("PayPal:ClientSecret is not configured. Set it via user-secrets, an environment " +
                         "variable, or your secret store before starting the app.");
        }

        if (string.IsNullOrWhiteSpace(options.Environment))
        {
            failures.Add($"PayPal:Environment is not configured. Set it to '{PayPalSettings.SandboxEnvironment}', " +
                         "or set PayPal:BaseUrl explicitly for any other environment.");
        }

        if (string.IsNullOrWhiteSpace(options.Currency))
        {
            failures.Add("PayPal:Currency is not configured. Set it to a three-letter ISO-4217 currency code.");
        }
        else if (options.Currency!.Trim().Length != 3)
        {
            failures.Add("PayPal:Currency must be a three-letter ISO-4217 currency code.");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (!Uri.TryCreate(options.BaseUrl!.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                failures.Add("PayPal:BaseUrl must be an absolute http or https URL when it is set.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(options.Environment) && !options.IsSandbox)
        {
            // The SDK declares a base URL for the sandbox only. Rather than inventing one for any
            // other environment — and silently sending live traffic to the sandbox, or the reverse —
            // require the operator to state it.
            failures.Add(
                $"PayPal:Environment is '{options.Environment}', but this PayPal SDK version only declares a " +
                $"base URL for '{PayPalSettings.SandboxEnvironment}'. Set PayPal:BaseUrl explicitly for that " +
                "environment, so test traffic cannot reach a live system by accident.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
