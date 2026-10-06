using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Square.Servers;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>
/// Square settings, bound from the <c>Square:</c> configuration section.
/// Values come from configuration only (user-secrets, environment, secret store) — never from code.
/// </summary>
public sealed class SquareSettings
{
    public const string SectionName = "Square";

    /// <summary><c>Square:Environment</c> — <c>sandbox</c> or <c>production</c>.</summary>
    public string? Environment { get; set; }

    /// <summary><c>Square:ApplicationId</c> — the OAuth client id.</summary>
    public string? ApplicationId { get; set; }

    /// <summary><c>Square:ApplicationSecret</c> — the OAuth client secret.</summary>
    public string? ApplicationSecret { get; set; }

    /// <summary><c>Square:RedirectUri</c> — the callback address registered with Square.</summary>
    public string? RedirectUri { get; set; }

    /// <summary>
    /// <c>Square:AccessToken</c> — optional. Used to act for the merchant while no merchant has
    /// connected through sign-in.
    /// </summary>
    public string? AccessToken { get; set; }

    public ServerEnvironment ServerEnvironment =>
        ServerEnvironment.TryGetKnownValue(Environment?.Trim().ToLowerInvariant(), out var env)
            ? env
            : throw new InvalidOperationException($"{SectionName}:{nameof(Environment)} is not a known Square environment.");

    public bool HasFallbackAccessToken => !string.IsNullOrWhiteSpace(AccessToken);
}

/// <summary>
/// Startup validation: the host refuses to start when a required Square setting is missing.
/// Messages name the key and never echo a value.
/// </summary>
public sealed class SquareSettingsValidator : IValidateOptions<SquareSettings>
{
    public ValidateOptionsResult Validate(string? name, SquareSettings settings)
    {
        var failures = new List<string>();

        var environment = settings.Environment?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(environment))
        {
            failures.Add($"{SquareSettings.SectionName}:{nameof(SquareSettings.Environment)} is not configured.");
        }
        else if (environment != ServerEnvironment.Sandbox.Value && environment != ServerEnvironment.Production.Value)
        {
            failures.Add($"{SquareSettings.SectionName}:{nameof(SquareSettings.Environment)} must be '{ServerEnvironment.Sandbox.Value}' or '{ServerEnvironment.Production.Value}'.");
        }

        Require(settings.ApplicationId, nameof(SquareSettings.ApplicationId), failures);
        Require(settings.ApplicationSecret, nameof(SquareSettings.ApplicationSecret), failures);
        Require(settings.RedirectUri, nameof(SquareSettings.RedirectUri), failures);

        if (!string.IsNullOrWhiteSpace(settings.RedirectUri)
            && (!Uri.TryCreate(settings.RedirectUri, UriKind.Absolute, out var redirect)
                || (redirect.Scheme != Uri.UriSchemeHttps && redirect.Scheme != Uri.UriSchemeHttp)))
        {
            failures.Add($"{SquareSettings.SectionName}:{nameof(SquareSettings.RedirectUri)} must be an absolute http(s) URL.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Require(string? value, string key, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{SquareSettings.SectionName}:{key} is not configured. Set it via user-secrets, environment or your secret store.");
        }
    }
}
