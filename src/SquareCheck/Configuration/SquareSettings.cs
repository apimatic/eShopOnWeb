using System.Net;
using Microsoft.Extensions.Configuration;
using Square.Servers;

namespace Microsoft.eShopWeb.SquareCheck.Configuration;

/// <summary>
/// The Square app this tool signs in to, bound from the <c>Square:</c> configuration section.
/// </summary>
public sealed record SquareSettings(
    ServerEnvironment Environment,
    string ApplicationId,
    string ApplicationSecret,
    Uri RedirectUri)
{
    public const string SectionName = "Square";
    public const string EnvironmentKey = "Square:Environment";
    public const string ApplicationIdKey = "Square:ApplicationId";
    public const string ApplicationSecretKey = "Square:ApplicationSecret";
    public const string RedirectUriKey = "Square:RedirectUri";

    /// <summary>
    /// Reads and validates the settings. Never echoes a configured value in an error — only key names.
    /// </summary>
    public static SettingsResult Load(IConfiguration configuration)
    {
        var errors = new List<string>();

        var missing = new[] { EnvironmentKey, ApplicationIdKey, ApplicationSecretKey, RedirectUriKey }
            .Where(key => string.IsNullOrWhiteSpace(configuration[key]))
            .ToList();
        if (missing.Count > 0)
        {
            errors.Add($"Missing configuration: {string.Join(", ", missing)}. " +
                       "Set them with 'dotnet user-secrets' or the SQUARE_* environment variables.");
        }

        ServerEnvironment? environment = null;
        var environmentValue = configuration[EnvironmentKey]?.Trim();
        if (!string.IsNullOrEmpty(environmentValue))
        {
            // Only the two hosted environments are supported: Custom would need a base URL this tool does not take.
            if (ServerEnvironment.TryGetKnownValue(environmentValue.ToLowerInvariant(), out var known) &&
                (known == ServerEnvironment.Sandbox || known == ServerEnvironment.Production))
            {
                environment = known;
            }
            else
            {
                errors.Add($"{EnvironmentKey} must be 'sandbox' or 'production'.");
            }
        }

        Uri? redirectUri = null;
        var redirectValue = configuration[RedirectUriKey]?.Trim();
        if (!string.IsNullOrEmpty(redirectValue))
        {
            if (TryParseRedirectUri(redirectValue, out var parsed, out var problem))
            {
                redirectUri = parsed;
            }
            else
            {
                errors.Add($"{RedirectUriKey} {problem}");
            }
        }

        if (errors.Count > 0)
        {
            return SettingsResult.Invalid(errors);
        }

        return SettingsResult.Valid(new SquareSettings(
            environment!,
            configuration[ApplicationIdKey]!.Trim(),
            configuration[ApplicationSecretKey]!.Trim(),
            redirectUri!));
    }

    private static bool TryParseRedirectUri(string value, out Uri? uri, out string problem)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
        {
            problem = "must be an absolute URL.";
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp)
        {
            problem = "must be an http:// address on this computer (the tool listens for the sign-in there).";
            return false;
        }

        if (!IsLoopback(parsed))
        {
            problem = "must point at this computer (localhost or a loopback address).";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.Fragment))
        {
            problem = "must not contain a fragment.";
            return false;
        }

        uri = parsed;
        problem = string.Empty;
        return true;
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback ||
        string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

    /// <summary>
    /// Never print the secret: records synthesise ToString from every member.
    /// </summary>
    public override string ToString() =>
        $"SquareSettings {{ Environment = {Environment.Value}, ApplicationId = {ApplicationId}, RedirectUri = {RedirectUri} }}";
}

public sealed class SettingsResult
{
    private SettingsResult(SquareSettings? settings, IReadOnlyList<string> errors)
    {
        Settings = settings;
        Errors = errors;
    }

    public SquareSettings? Settings { get; }

    public IReadOnlyList<string> Errors { get; }

    public static SettingsResult Valid(SquareSettings settings) => new(settings, []);

    public static SettingsResult Invalid(IReadOnlyList<string> errors) => new(null, errors);
}
