using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Square.Servers;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>
/// Settings bound from the <c>Square:</c> configuration section. Values come from configuration
/// (user-secrets, environment, a secret store) and are never hard-coded.
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
    /// <c>Square:AccessToken</c> — optional. A token the merchant already granted; used while no merchant has
    /// connected through sign-in.
    /// </summary>
    public string? AccessToken { get; set; }

    public bool HasConfiguredAccessToken => !string.IsNullOrWhiteSpace(AccessToken);

    public ServerEnvironment ServerEnvironment =>
        TryParseEnvironment(Environment, out var environment)
            ? environment
            : throw new InvalidOperationException("Square:Environment is not configured.");

    public static bool TryParseEnvironment(string? value, out ServerEnvironment environment)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "sandbox":
                environment = ServerEnvironment.Sandbox;
                return true;
            case "production":
                environment = ServerEnvironment.Production;
                return true;
            default:
                environment = ServerEnvironment.Sandbox;
                return false;
        }
    }
}

/// <summary>
/// Refuses to start the host when a required Square setting is missing. Messages name the key, never the value.
/// </summary>
public sealed class SquareSettingsValidator : IValidateOptions<SquareSettings>
{
    public ValidateOptionsResult Validate(string? name, SquareSettings settings)
    {
        var failures = new List<string>();

        if (!SquareSettings.TryParseEnvironment(settings.Environment, out _))
            failures.Add("Square:Environment must be set to 'sandbox' or 'production'.");
        if (string.IsNullOrWhiteSpace(settings.ApplicationId))
            failures.Add("Square:ApplicationId is not configured.");
        if (string.IsNullOrWhiteSpace(settings.ApplicationSecret))
            failures.Add("Square:ApplicationSecret is not configured.");
        if (string.IsNullOrWhiteSpace(settings.RedirectUri))
            failures.Add("Square:RedirectUri is not configured.");
        else if (!Uri.TryCreate(settings.RedirectUri, UriKind.Absolute, out _))
            failures.Add("Square:RedirectUri must be an absolute URL.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
