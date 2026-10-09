namespace Microsoft.eShopWeb.SquareCheck.SignIn;

/// <summary>
/// The authorization-code prompt handed to the Square SDK: opens the browser at Square's sign-in page
/// (once per run), waits for the operator to come back to the redirect address, and returns the code.
/// </summary>
public sealed class BrowserSignIn
{
    private readonly OAuthCallbackListener _listener;
    private readonly IBrowserLauncher _browser;
    private readonly TextWriter _output;
    private readonly TimeSpan _timeout;
    private readonly Action? _onCodeReceived;
    private int _attempted;

    /// <param name="onCodeReceived">Called once the code is in hand, before the SDK exchanges it.</param>
    public BrowserSignIn(
        OAuthCallbackListener listener,
        IBrowserLauncher browser,
        TextWriter output,
        TimeSpan timeout,
        Action? onCodeReceived = null)
    {
        _listener = listener;
        _browser = browser;
        _output = output;
        _timeout = timeout;
        _onCodeReceived = onCodeReceived;
    }

    /// <summary>
    /// Whether the sign-in page has been opened during this run.
    /// </summary>
    public bool Attempted => Volatile.Read(ref _attempted) == 1;

    /// <summary>
    /// Matches the SDK's <c>AuthorizationCodePrompt</c> delegate.
    /// </summary>
    public async Task<string> PromptAsync(string authorizationUrl, CancellationToken cancellationToken)
    {
        // The SDK asks again if a later call is answered 401; the operator is never sent to sign in twice.
        if (Interlocked.Exchange(ref _attempted, 1) == 1)
        {
            throw new SignInAlreadyAttemptedException();
        }

        cancellationToken.ThrowIfCancellationRequested();

        _output.WriteLine("Opening your browser to sign in to Square and approve SquareCheck's access...");
        if (!_browser.TryOpen(authorizationUrl))
        {
            _output.WriteLine("Could not open a browser automatically.");
        }

        _output.WriteLine($"If the sign-in page did not open, visit this address:{Environment.NewLine}  {authorizationUrl}");
        _output.WriteLine($"Waiting up to {Describe(_timeout)} for you to finish signing in (Ctrl+C to stop)...");

        SignInCallback callback;
        try
        {
            callback = await _listener.WaitForCallbackAsync(cancellationToken)
                .WaitAsync(_timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _listener.Complete(BrowserPage.Expired);
            throw new SignInTimedOutException(_timeout);
        }

        if (!callback.Approved)
        {
            throw new SignInDeclinedException(callback.Error, callback.ErrorDescription);
        }

        _onCodeReceived?.Invoke();
        return callback.Code!;
    }

    internal static string Describe(TimeSpan duration) =>
        duration.TotalMinutes >= 1 && duration.Seconds == 0
            ? $"{duration.TotalMinutes:0} minute{(duration.TotalMinutes == 1 ? "" : "s")}"
            : $"{duration.TotalSeconds:0.#} seconds";
}
