using System.Net;

namespace SquareCheck;

// Orchestrates the browser sign-in step of the OAuth2 authorization-code flow.
// The caller wires PromptCallback into OAuth2AuthorizationCodeCredentials and
// calls ServeSuccessAsync once the merchant name is known.
internal sealed class OAuthFlowCoordinator
{
    private readonly IRedirectListener _listener;
    private readonly IProcessLauncher _launcher;
    private readonly string _expectedState;
    private readonly TimeSpan _signInTimeout;
    private bool _browserOpened;

    public bool IsTimedOut { get; private set; }
    public bool IsDeclined { get; private set; }

    public OAuthFlowCoordinator(
        IRedirectListener listener,
        IProcessLauncher launcher,
        string expectedState,
        TimeSpan signInTimeout = default)
    {
        _listener = listener;
        _launcher = launcher;
        _expectedState = expectedState;
        _signInTimeout = signInTimeout == default ? TimeSpan.FromMinutes(5) : signInTimeout;
    }

    public static string GenerateState() =>
        Guid.NewGuid().ToString("N");

    // Returns the delegate to supply as PromptForAuthorizationCode.
    // appCt is the application-level token (Ctrl+C); it distinguishes user cancellation
    // from the internal 5-minute sign-in timeout.
    public Func<string, CancellationToken, Task<string>> CreatePromptCallback(CancellationToken appCt) =>
        async (authUrl, sdkCt) =>
        {
            if (!_browserOpened)
            {
                _launcher.OpenUrl(authUrl);
                _browserOpened = true;
            }

            using var timeoutCts = new CancellationTokenSource(_signInTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                appCt, sdkCt, timeoutCts.Token);

            while (true)
            {
                OAuthCallback callback;
                try
                {
                    callback = await _listener.WaitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Set IsTimedOut only when neither the app nor the SDK cancelled —
                    // the internal timeout must have fired.
                    if (!appCt.IsCancellationRequested && !sdkCt.IsCancellationRequested)
                        IsTimedOut = true;
                    throw;
                }

                // Square returns error=access_denied when the operator clicks "Deny".
                if (callback.Error != null)
                {
                    await _listener.RespondAsync(
                        400, "text/html",
                        ErrorHtml("Access was declined. You may close this tab."),
                        CancellationToken.None);
                    IsDeclined = true;
                    throw new InvalidOperationException(
                        $"OAuth error from Square: {callback.Error}");
                }

                // Reject requests that do not carry this run's state value.
                // Stale tabs from earlier runs will have a different state.
                if (callback.State != _expectedState)
                {
                    await _listener.RespondAsync(
                        400, "text/html",
                        ErrorHtml("This sign-in link is no longer valid. You may close this tab."),
                        CancellationToken.None);
                    continue; // keep listening for the real redirect
                }

                if (string.IsNullOrEmpty(callback.Code))
                {
                    await _listener.RespondAsync(
                        400, "text/html",
                        ErrorHtml("No authorization code received. You may close this tab."),
                        CancellationToken.None);
                    IsDeclined = true;
                    throw new InvalidOperationException("No authorization code in redirect.");
                }

                // Do NOT respond to the browser yet — the main flow will call
                // ServeSuccessAsync once it has the merchant name.
                return callback.Code;
            }
        };

    // Call this after the merchant name is known to complete the browser's pending request.
    public Task ServeSuccessAsync(string businessName, CancellationToken ct)
    {
        var name = WebUtility.HtmlEncode(businessName);
        return _listener.RespondAsync(200, "text/html", SuccessHtml(name), ct);
    }

    private static string SuccessHtml(string escapedName) => $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <title>Connected to Square</title>
          <style>body{font-family:sans-serif;max-width:600px;margin:4rem auto;text-align:center}</style>
        </head>
        <body>
          <h1>Connected to {{escapedName}}</h1>
          <p>You may close this tab.</p>
        </body>
        </html>
        """;

    private static string ErrorHtml(string message) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="utf-8"><title>Sign-in failed</title></head>
        <body>
          <h1>Sign-in failed</h1>
          <p>{WebUtility.HtmlEncode(message)}</p>
        </body>
        </html>
        """;
}
