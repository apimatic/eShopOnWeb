using System.Net;

namespace Microsoft.eShopWeb.SquareCheck.SignIn;

/// <summary>A page the tool shows in the operator's browser on the redirect address.</summary>
public sealed record SignInPage(HttpStatusCode Status, string Title, string Html);

public static class SignInPages
{
    public static SignInPage Connected(string businessName) => Page(HttpStatusCode.OK,
        "Connected to Square",
        $"<p>SquareCheck is now connected to the Square business <strong>{Encode(businessName)}</strong>.</p>"
        + "<p>You can close this tab and return to the terminal.</p>");

    public static SignInPage Declined() => Page(HttpStatusCode.OK,
        "Access declined",
        "<p>You declined access, so SquareCheck is not connected to your Square account.</p>"
        + "<p>You can close this tab.</p>");

    public static SignInPage Failed() => Page(HttpStatusCode.OK,
        "Not connected",
        "<p>SquareCheck could not finish connecting to Square. The terminal says why.</p>"
        + "<p>You can close this tab.</p>");

    public static SignInPage Cancelled() => Page(HttpStatusCode.OK,
        "Sign-in cancelled",
        "<p>The sign-in was cancelled in the terminal. SquareCheck is not connected.</p>"
        + "<p>You can close this tab.</p>");

    public static SignInPage NotThisSignIn() => Page(HttpStatusCode.BadRequest,
        "Not the current sign-in",
        "<p>This link does not belong to the sign-in SquareCheck is waiting for. It may be from an earlier run.</p>"
        + "<p>Nothing was connected. Use the Square page SquareCheck opened for this run.</p>");

    public static SignInPage NotFound() => Page(HttpStatusCode.NotFound, "Not found", "<p>Nothing here.</p>");

    private static SignInPage Page(HttpStatusCode status, string title, string body) => new(status, title,
        "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">"
        + $"<title>{Encode(title)} - SquareCheck</title>"
        + "<style>body{font-family:system-ui,sans-serif;max-width:36rem;margin:4rem auto;padding:0 1rem;line-height:1.5}</style>"
        + $"</head><body><h1>{Encode(title)}</h1>{body}</body></html>");

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
