using Microsoft.eShopWeb.SquareCheck.Configuration;
using Microsoft.eShopWeb.SquareCheck.SignIn;
using Microsoft.eShopWeb.SquareCheck.SquareAccess;

namespace Microsoft.eShopWeb.SquareCheck;

public sealed record SquareCheckTimings(TimeSpan SignInWindow, TimeSpan SquareBudget)
{
    /// <summary>
    /// Five minutes for the operator to sign in (two-factor codes, password resets); then one deadline
    /// covering every Square call that follows (token exchange, merchant, locations).
    /// </summary>
    public static SquareCheckTimings Default { get; } = new(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(60));
}

/// <summary>
/// One run: sign in through the operator's browser, then show the connected account.
/// Writes the account to <c>stdout</c>; progress and the single failure line go to <c>stderr</c>.
/// </summary>
public sealed class SquareCheckApp
{
    private readonly SquareConnectionSettings _settings;
    private readonly SquareClientFactory _clients;
    private readonly IBrowserLauncher _browser;
    private readonly TextWriter _out;
    private readonly TextWriter _status;
    private readonly SquareCheckTimings _timings;

    public SquareCheckApp(
        SquareConnectionSettings settings,
        HttpClient squareHttpClient,
        IBrowserLauncher browser,
        TextWriter output,
        TextWriter status,
        SquareCheckTimings? timings = null)
    {
        _settings = settings;
        _clients = new SquareClientFactory(squareHttpClient, settings);
        _browser = browser;
        _out = output;
        _status = status;
        _timings = timings ?? SquareCheckTimings.Default;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await SignInAndShowAccountAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _status.WriteLine("Cancelled.");
            return ExitCodes.Cancelled;
        }
    }

    private async Task<int> SignInAndShowAccountAsync(CancellationToken cancellationToken)
    {
        RedirectListener listener;
        try
        {
            listener = RedirectListener.Start(_settings.RedirectAddress);
        }
        catch (RedirectListenerException ex)
        {
            _status.WriteLine(ex.Message);
            return ExitCodes.SetupProblem;
        }

        using (listener)
        {
            var request = AuthorizationRequest.Create(_clients.BaseUrl, _settings.ApplicationId, _settings.RedirectUri);

            // Opened exactly once per run; the printed address is the fallback, not a second attempt.
            _status.WriteLine($"Opening your browser to sign in to Square ({_settings.EnvironmentName})...");
            if (!_browser.TryOpen(request.SignInPage))
            {
                _status.WriteLine("Could not open a browser automatically.");
            }

            _status.WriteLine($"If the Square page did not open, visit: {request.SignInPage.AbsoluteUri}");
            _status.WriteLine($"Waiting up to {Describe(_timings.SignInWindow)} for you to sign in and approve access (Ctrl+C to stop)...");

            AuthorizationCallback callback;
            using (var signInWindow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                signInWindow.CancelAfter(_timings.SignInWindow);
                try
                {
                    callback = await listener.WaitForCallbackAsync(request, signInWindow.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _status.WriteLine($"Sign-in was not completed within {Describe(_timings.SignInWindow)}; nothing was connected.");
                    return ExitCodes.SignInNotCompleted;
                }
            }

            return callback switch
            {
                ApprovedCallback approved => await ConnectAsync(approved, cancellationToken).ConfigureAwait(false),
                DeniedCallback { OperatorDeclined: true } declined => await DeclinedAsync(declined).ConfigureAwait(false),
                DeniedCallback denied => await RefusedAsync(denied).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unhandled sign-in result {callback.GetType().Name}."),
            };
        }
    }

    private async Task<int> DeclinedAsync(DeniedCallback declined)
    {
        await declined.Reply.SendAsync(SignInPages.Declined()).ConfigureAwait(false);
        _status.WriteLine("Access was declined in Square; nothing was connected.");
        return ExitCodes.SignInNotCompleted;
    }

    private async Task<int> RefusedAsync(DeniedCallback denied)
    {
        await denied.Reply.SendAsync(SignInPages.Failed()).ConfigureAwait(false);
        var detail = string.IsNullOrWhiteSpace(denied.Description) ? denied.Error : $"{denied.Error}: {denied.Description}";
        _status.WriteLine($"Square refused the sign-in ({OneLine(detail)}).");
        return ExitCodes.SquareFailed;
    }

    private async Task<int> ConnectAsync(ApprovedCallback approved, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timings.SquareBudget);
        try
        {
            var accessToken = await new SquareTokenExchange(_clients.CreateForTokenExchange(), _settings)
                .ExchangeAsync(approved.Code, deadline.Token).ConfigureAwait(false);

            var account = new SquareAccountReader(_clients.CreateSignedIn(accessToken));
            var merchant = await account.GetMerchantAsync(deadline.Token).ConfigureAwait(false);
            await approved.Reply.SendAsync(SignInPages.Connected(merchant.DisplayName)).ConfigureAwait(false);

            var locations = await account.ListLocationsAsync(deadline.Token).ConfigureAwait(false);
            AccountReport.Write(_out, _settings.EnvironmentName, merchant, locations);
            return ExitCodes.Success;
        }
        catch (SquareRequestException ex)
        {
            await approved.Reply.SendAsync(SignInPages.Failed()).ConfigureAwait(false);
            _status.WriteLine(ex.Message);
            return ExitCodes.SquareFailed;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await approved.Reply.SendAsync(SignInPages.Failed()).ConfigureAwait(false);
            _status.WriteLine($"Square did not answer within {Describe(_timings.SquareBudget)}.");
            return ExitCodes.SquareFailed;
        }
        catch (OperationCanceledException)
        {
            await approved.Reply.SendAsync(SignInPages.Cancelled()).ConfigureAwait(false);
            throw;
        }
    }

    private static string Describe(TimeSpan span) =>
        span.TotalMinutes >= 1 && span.Seconds == 0 ? $"{span.TotalMinutes:0} minute{(span.TotalMinutes == 1 ? "" : "s")}"
        : span.TotalSeconds >= 1 ? $"{span.TotalSeconds:0.#} seconds"
        : $"{span.TotalMilliseconds:0} ms";

    private static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
}
