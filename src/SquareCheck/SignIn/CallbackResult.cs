namespace SquareCheck.SignIn;

internal abstract record CallbackResult
{
    internal sealed record Success(string Code) : CallbackResult;
    internal sealed record Declined(string ErrorCode) : CallbackResult;
    internal sealed record Stale : CallbackResult;
}
