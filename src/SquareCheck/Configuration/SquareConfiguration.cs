using System.Collections;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.SquareCheck.Configuration;

/// <summary>
/// Builds the tool's configuration: .NET user-secrets, overlaid by the SQUARE_* environment variables.
/// </summary>
public static class SquareConfiguration
{
    /// <summary>
    /// The documented environment variable for each configuration key.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> EnvironmentVariableAliases =
        new Dictionary<string, string>
        {
            ["SQUARE_ENVIRONMENT"] = SquareSettings.EnvironmentKey,
            ["SQUARE_APPLICATION_ID"] = SquareSettings.ApplicationIdKey,
            ["SQUARE_APPLICATION_SECRET"] = SquareSettings.ApplicationSecretKey,
            ["SQUARE_REDIRECT_URI"] = SquareSettings.RedirectUriKey,
        };

    public static IConfiguration Build(IDictionary environmentVariables)
    {
        return new ConfigurationBuilder()
            .AddUserSecrets(typeof(SquareConfiguration).Assembly, optional: true)
            .AddInMemoryCollection(MapAliases(environmentVariables))
            .Build();
    }

    internal static IEnumerable<KeyValuePair<string, string?>> MapAliases(IDictionary environmentVariables)
    {
        foreach (var (variable, key) in EnvironmentVariableAliases)
        {
            if (environmentVariables[variable] is string value && !string.IsNullOrWhiteSpace(value))
            {
                yield return new KeyValuePair<string, string?>(key, value);
            }
        }
    }
}
