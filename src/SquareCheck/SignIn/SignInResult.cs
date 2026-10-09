using Square.Models;

namespace SquareCheck.SignIn;

internal sealed record SignInResult
{
    public required string AccessToken { get; init; }
    public required Merchant Merchant { get; init; }
}
