using System.Net;

namespace Microsoft.eShopWeb.SquareCheck.SignIn;

/// <summary>
/// A small HTML page shown in the operator's browser at the redirect address.
/// </summary>
public sealed record BrowserPage(int StatusCode, string Title, string Message)
{
    public static BrowserPage Connected(string? businessName) => new(
        StatusCodes.Ok,
        "Connected to Square",
        string.IsNullOrWhiteSpace(businessName)
            ? "SquareCheck is now connected to your Square business. You can close this tab."
            : $"SquareCheck is now connected to the Square business “{businessName}”. You can close this tab.");

    public static BrowserPage Declined { get; } = new(
        StatusCodes.Ok,
        "Access not approved",
        "Access to your Square account was not approved, so SquareCheck did not connect. You can close this tab.");

    public static BrowserPage NotThisSignIn { get; } = new(
        StatusCodes.BadRequest,
        "Not a current sign-in",
        "This link does not belong to the sign-in SquareCheck is waiting for (it may be from an earlier run). " +
        "Nothing was connected. Return to the terminal, or run SquareCheck again to sign in.");

    public static BrowserPage AlreadyHandled { get; } = new(
        StatusCodes.Conflict,
        "Sign-in already handled",
        "SquareCheck has already handled a sign-in for this run. Return to the terminal.");

    public static BrowserPage NotFound { get; } = new(
        StatusCodes.NotFound,
        "Not found",
        "SquareCheck only answers at its sign-in redirect address.");

    public static BrowserPage Expired { get; } = new(
        StatusCodes.Ok,
        "Sign-in not completed",
        "SquareCheck stopped waiting for this sign-in. Nothing was connected. Run SquareCheck again to sign in.");

    public static BrowserPage Failed { get; } = new(
        StatusCodes.Ok,
        "Could not connect",
        "SquareCheck could not finish connecting to Square. Return to the terminal for details.");

    public string ToHtml()
    {
        var title = WebUtility.HtmlEncode(Title);
        var message = WebUtility.HtmlEncode(Message);
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>SquareCheck – {{title}}</title>
              <style>body{font-family:system-ui,sans-serif;max-width:40rem;margin:4rem auto;padding:0 1rem;line-height:1.5}</style>
            </head>
            <body>
              <h1>{{title}}</h1>
              <p>{{message}}</p>
            </body>
            </html>
            """;
    }

    private static class StatusCodes
    {
        public const int Ok = 200;
        public const int BadRequest = 400;
        public const int NotFound = 404;
        public const int Conflict = 409;
    }
}
