using Microsoft.Extensions.Configuration;
using Square.Servers;

namespace SquareCheck;

internal sealed class SquareSettings
{
    public required string ApplicationId { get; init; }
    public required string ApplicationSecret { get; init; }
    public required string RedirectUri { get; init; }
    public required ServerEnvironment SquareEnvironment { get; init; }

    public static SquareSettings Load(IConfiguration config)
    {
        var s = config.GetSection("Square");

        var env = s["Environment"];
        var appId = s["ApplicationId"];
        var appSecret = s["ApplicationSecret"];
        var redirectUri = s["RedirectUri"];

        var missing = new List<string>(4);
        if (string.IsNullOrWhiteSpace(env)) missing.Add("Square:Environment (SQUARE_ENVIRONMENT)");
        if (string.IsNullOrWhiteSpace(appId)) missing.Add("Square:ApplicationId (SQUARE_APPLICATION_ID)");
        if (string.IsNullOrWhiteSpace(appSecret)) missing.Add("Square:ApplicationSecret (SQUARE_APPLICATION_SECRET)");
        if (string.IsNullOrWhiteSpace(redirectUri)) missing.Add("Square:RedirectUri (SQUARE_REDIRECT_URI)");

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Required configuration is missing. Set these via environment variables or " +
                $"dotnet user-secrets: {string.Join(", ", missing)}");

        var squareEnv = env!.Equals("sandbox", StringComparison.OrdinalIgnoreCase)
            ? ServerEnvironment.Sandbox
            : ServerEnvironment.Production;

        return new SquareSettings
        {
            ApplicationId = appId!,
            ApplicationSecret = appSecret!,
            RedirectUri = redirectUri!,
            SquareEnvironment = squareEnv,
        };
    }
}
