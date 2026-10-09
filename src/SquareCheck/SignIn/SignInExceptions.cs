namespace SquareCheck.SignIn;

internal sealed class SignInTimeoutException : Exception
{
    public SignInTimeoutException() : base("Sign-in timed out after 5 minutes.") { }
}

internal sealed class SignInDeclinedException : Exception
{
    public SignInDeclinedException() : base("Operator declined access.") { }
}

internal sealed class SquareApiException : Exception
{
    public SquareApiException(string message) : base(message) { }
    public SquareApiException(string message, Exception inner) : base(message, inner) { }
}
