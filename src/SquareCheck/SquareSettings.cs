using Microsoft.Extensions.Configuration;

namespace SquareCheck;

internal sealed class SquareSettings
{
    public string Environment { get; init; } = "";
    public string ApplicationId { get; init; } = "";
    public string ApplicationSecret { get; init; } = "";
    public string RedirectUri { get; init; } = "";

    public static SquareSettings Bind(IConfiguration config) => new()
    {
        Environment = config["Square:Environment"] ?? "",
        ApplicationId = config["Square:ApplicationId"] ?? "",
        ApplicationSecret = config["Square:ApplicationSecret"] ?? "",
        RedirectUri = config["Square:RedirectUri"] ?? "",
    };

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Environment))
            Fail("Square:Environment", "SQUARE_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(ApplicationId))
            Fail("Square:ApplicationId", "SQUARE_APPLICATION_ID");
        if (string.IsNullOrWhiteSpace(ApplicationSecret))
            Fail("Square:ApplicationSecret", "SQUARE_APPLICATION_SECRET");
        if (string.IsNullOrWhiteSpace(RedirectUri))
            Fail("Square:RedirectUri", "SQUARE_REDIRECT_URI");

        static void Fail(string key, string envVar) =>
            throw new InvalidOperationException(
                $"{key} is not configured. Set {envVar} or add it to user-secrets.");
    }
}
