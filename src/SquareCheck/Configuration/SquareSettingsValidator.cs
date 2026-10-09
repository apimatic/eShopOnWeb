using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.Configuration;
using Square.Servers;

namespace Microsoft.eShopWeb.SquareCheck.Configuration;

/// <summary>
/// Refuses to let the tool start with a missing, blank or unusable setting. Problems name the
/// configuration key, never its value.
/// </summary>
public static class SquareSettingsValidator
{
    private const string EnvironmentKey = "Square:Environment";
    private const string ApplicationIdKey = "Square:ApplicationId";
    private const string ApplicationSecretKey = "Square:ApplicationSecret";
    private const string RedirectUriKey = "Square:RedirectUri";

    public static bool TryValidate(
        IConfiguration configuration,
        [NotNullWhen(true)] out SquareConnectionSettings? settings,
        out IReadOnlyList<string> problems)
    {
        var raw = configuration.GetSection(SquareSettings.SectionName).Get<SquareSettings>() ?? new SquareSettings();
        return TryValidate(raw, out settings, out problems);
    }

    public static bool TryValidate(
        SquareSettings raw,
        [NotNullWhen(true)] out SquareConnectionSettings? settings,
        out IReadOnlyList<string> problems)
    {
        var found = new List<string>();

        var environmentName = raw.Environment?.Trim().ToLowerInvariant();
        ServerEnvironment? environment = environmentName switch
        {
            null or "" => null,
            "sandbox" => ServerEnvironment.Sandbox,
            "production" => ServerEnvironment.Production,
            _ => null,
        };
        if (string.IsNullOrEmpty(environmentName))
        {
            found.Add($"{EnvironmentKey} is not set (use \"sandbox\" or \"production\")");
        }
        else if (environment is null)
        {
            found.Add($"{EnvironmentKey} must be \"sandbox\" or \"production\"");
        }

        RequireValue(raw.ApplicationId, ApplicationIdKey, found);
        RequireValue(raw.ApplicationSecret, ApplicationSecretKey, found);

        var redirectUri = raw.RedirectUri?.Trim();
        if (string.IsNullOrEmpty(redirectUri))
        {
            found.Add($"{RedirectUriKey} is not set");
        }
        else if (DescribeRedirectProblem(redirectUri) is { } redirectProblem)
        {
            found.Add($"{RedirectUriKey} {redirectProblem}");
        }

        problems = found;
        if (found.Count > 0)
        {
            settings = null;
            return false;
        }

        settings = new SquareConnectionSettings(
            environmentName!,
            environment!,
            raw.ApplicationId!.Trim(),
            raw.ApplicationSecret!.Trim(),
            redirectUri!);
        return true;
    }

    private static void RequireValue(string? value, string key, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"{key} is not set");
        }
    }

    private static string? DescribeRedirectProblem(string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri))
        {
            return "is not an absolute URL";
        }

        // The tool receives Square's redirect itself, on the operator's machine, over plain HTTP.
        if (uri.Scheme != Uri.UriSchemeHttp)
        {
            return "must be an http:// address on this computer (for example http://localhost:<port>/callback)";
        }

        if (!IsLoopback(uri))
        {
            return "must point at this computer (localhost, 127.0.0.1 or [::1])";
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return "must not carry a query string or fragment";
        }

        return null;
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback
        || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
}
