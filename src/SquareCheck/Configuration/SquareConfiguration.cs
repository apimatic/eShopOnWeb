using System.Collections;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.SquareCheck.Configuration;

/// <summary>
/// Builds the tool's configuration. Later sources win:
/// .NET user-secrets → <c>Square__*</c> environment variables → the <c>SQUARE_*</c> environment variables.
/// </summary>
public static class SquareConfiguration
{
    /// <summary>The environment variables the credentials arrive in, and the configuration key each one feeds.</summary>
    public static readonly IReadOnlyDictionary<string, string> EnvironmentVariableKeys = new Dictionary<string, string>
    {
        ["SQUARE_ENVIRONMENT"] = "Square:Environment",
        ["SQUARE_APPLICATION_ID"] = "Square:ApplicationId",
        ["SQUARE_APPLICATION_SECRET"] = "Square:ApplicationSecret",
        ["SQUARE_REDIRECT_URI"] = "Square:RedirectUri",
    };

    public static IConfiguration Load() =>
        Load(Environment.GetEnvironmentVariables(), includeUserSecrets: true);

    public static IConfiguration Load(IDictionary environmentVariables, bool includeUserSecrets)
    {
        var builder = new ConfigurationBuilder();
        if (includeUserSecrets)
        {
            builder.AddUserSecrets(typeof(SquareConfiguration).Assembly, optional: true);
        }

        builder.AddEnvironmentVariables();
        builder.AddInMemoryCollection(MapSquareVariables(environmentVariables));
        return builder.Build();
    }

    internal static IEnumerable<KeyValuePair<string, string?>> MapSquareVariables(IDictionary environmentVariables)
    {
        foreach (var (variable, key) in EnvironmentVariableKeys)
        {
            // A blank variable does not hide a value stored in user-secrets.
            if (environmentVariables[variable] is string value && !string.IsNullOrWhiteSpace(value))
            {
                yield return new KeyValuePair<string, string?>(key, value);
            }
        }
    }
}
