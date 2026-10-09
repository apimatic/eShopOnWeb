namespace Microsoft.eShopWeb.SquareCheck.SignIn;

/// <summary>
/// The merchant did not approve the shop's access (or Square reported an error instead of a code).
/// </summary>
public sealed class SignInDeclinedException(string? error, string? errorDescription)
    : Exception("Access to the Square account was not approved.")
{
    public string? Error { get; } = error;

    public string? ErrorDescription { get; } = errorDescription;
}

/// <summary>
/// The operator did not finish signing in within the allowed time.
/// </summary>
public sealed class SignInTimedOutException(TimeSpan timeout)
    : Exception($"Sign-in was not completed within {timeout}.")
{
    public TimeSpan Timeout { get; } = timeout;
}

/// <summary>
/// Something asked for a second sign-in during one run; the sign-in page is opened only once per run.
/// </summary>
public sealed class SignInAlreadyAttemptedException()
    : Exception("Square asked to sign in again, but this run has already signed in once.");
