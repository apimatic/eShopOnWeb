using System.Security.Cryptography;
using Microsoft.eShopWeb.SquareCheck.Configuration;
using Microsoft.eShopWeb.SquareCheck.SignIn;
using Microsoft.eShopWeb.SquareCheck.SquareAccess;

namespace Microsoft.eShopWeb.SquareCheck;

/// <param name="SignIn">How long the operator has to finish signing in, from the moment the browser opens.</param>
/// <param name="Request">The budget for each phase of talking to Square (token exchange + merchant, then locations).</param>
public sealed record SquareCheckTimeouts(TimeSpan SignIn, TimeSpan Request)
{
    public static SquareCheckTimeouts Default { get; } = new(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30));
}

/// <summary>
/// One run: sign in through the browser, then show the merchant and its locations.
/// </summary>
public sealed class SquareCheckApp
{
    private readonly SquareSettings _settings;
    private readonly IBrowserLauncher _browser;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly SquareCheckTimeouts _timeouts;
    private readonly Func<HttpMessageHandler>? _httpHandlerFactory;

    /// <param name="httpHandlerFactory">Replaces the network handler (tests); <c>null</c> for the real network.</param>
    public SquareCheckApp(
        SquareSettings settings,
        IBrowserLauncher browser,
        TextWriter output,
        TextWriter error,
        SquareCheckTimeouts timeouts,
        Func<HttpMessageHandler>? httpHandlerFactory = null)
    {
        _settings = settings;
        _browser = browser;
        _output = output;
        _error = error;
        _timeouts = timeouts;
        _httpHandlerFactory = httpHandlerFactory;
    }

    /// <summary>
    /// Reports the address the redirect listener actually bound (tests bind port 0 and play the browser).
    /// </summary>
    internal Action<Uri>? OnListening { get; init; }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        // Ties the redirect back to this run: a visit without it (an old tab, a forwarded link) is ignored.
        var state = NewState();

        OAuthCallbackListener listener;
        try
        {
            listener = await OAuthCallbackListener.StartAsync(_settings.RedirectUri, state, cancellationToken).ConfigureAwait(false);
        }
        catch (RedirectAddressUnavailableException ex)
        {
            _error.WriteLine(ex.Message);
            return ExitCodes.CannotStart;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _error.WriteLine("Cancelled.");
            return ExitCodes.Cancelled;
        }

        await using (listener.ConfigureAwait(false))
        {
            OnListening?.Invoke(listener.CallbackUri);

            // Phase 1 — sign-in, token exchange, merchant. Bounded overall; once the code arrives the
            // remaining budget shrinks to the request budget.
            using var signInPhase = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            signInPhase.CancelAfter(_timeouts.SignIn + _timeouts.Request);

            var signIn = new BrowserSignIn(
                listener,
                _browser,
                _output,
                _timeouts.SignIn,
                onCodeReceived: () => signInPhase.CancelAfter(_timeouts.Request));

            using var httpClient = SquareClientFactory.CreateHttpClient(_httpHandlerFactory?.Invoke());
            var client = SquareClientFactory.Create(_settings, state, signIn.PromptAsync, httpClient);
            var reader = new SquareAccountReader(client);

            try
            {
                var merchant = await reader.GetMerchantAsync(signInPhase.Token).ConfigureAwait(false);
                listener.Complete(BrowserPage.Connected(merchant.BusinessName));

                // Phase 2 — locations.
                using var locationsPhase = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                locationsPhase.CancelAfter(_timeouts.Request);
                var locations = await reader.GetLocationsAsync(locationsPhase.Token).ConfigureAwait(false);

                AccountPrinter.Print(_output, _settings.Environment, merchant, locations);
                return ExitCodes.Success;
            }
            catch (Exception ex)
            {
                var failure = FailureClassifier.Classify(ex, cancellationToken, _timeouts.Request);
                listener.Complete(failure.Page);
                _error.WriteLine(failure.Message);
                return failure.ExitCode;
            }
        }
    }

    private static string NewState() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
