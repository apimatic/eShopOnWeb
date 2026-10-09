namespace Microsoft.eShopWeb.SquareCheck;

public static class ExitCodes
{
    /// <summary>Signed in and showed the account.</summary>
    public const int Success = 0;

    /// <summary>Square refused or failed a request (including the sign-in's token exchange).</summary>
    public const int SquareFailure = 1;

    /// <summary>The operator declined access, or did not finish signing in in time.</summary>
    public const int SignInNotCompleted = 2;

    /// <summary>The tool could not start: missing/invalid configuration, or the redirect port is unavailable.</summary>
    public const int CannotStart = 3;

    /// <summary>The operator pressed Ctrl+C (128 + SIGINT).</summary>
    public const int Cancelled = 130;
}
