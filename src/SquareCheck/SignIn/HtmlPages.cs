using System.Web;

namespace SquareCheck.SignIn;

internal static class HtmlPages
{
    private const string BaseStyle =
        "<style>*{box-sizing:border-box}body{font-family:system-ui,sans-serif;" +
        "max-width:520px;margin:80px auto;padding:0 24px;text-align:center;color:#1a1a1a}" +
        "h1{font-size:1.5rem;margin-bottom:.5rem}p{color:#555;margin:0}</style>";

    internal static string Success(string businessName) =>
        $"""
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="utf-8"><title>Connected – Square</title>{BaseStyle}</head>
        <body>
          <h1>Connected to {HttpUtility.HtmlEncode(businessName)}</h1>
          <p>You may close this tab and return to the terminal.</p>
        </body>
        </html>
        """;

    internal static readonly string Declined =
        """
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="utf-8"><title>Sign-in declined – Square</title></head>
        <body>
          <h1>Sign-in declined</h1>
          <p>Access was not approved. You may close this tab.</p>
        </body>
        </html>
        """;

    internal static readonly string Stale =
        """
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="utf-8"><title>Link expired – Square</title></head>
        <body>
          <h1>This link has expired</h1>
          <p>This sign-in link belongs to a previous run. Please run the tool again.</p>
        </body>
        </html>
        """;

    internal static readonly string Error =
        """
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="utf-8"><title>Error – Square</title></head>
        <body>
          <h1>An error occurred</h1>
          <p>Square could not complete sign-in. See the terminal for details.</p>
        </body>
        </html>
        """;
}
