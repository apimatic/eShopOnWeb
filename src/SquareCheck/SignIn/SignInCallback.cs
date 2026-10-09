namespace Microsoft.eShopWeb.SquareCheck.SignIn;

/// <summary>
/// What the browser brought back to the redirect address for this run's sign-in.
/// </summary>
/// <param name="Code">The authorization code; <c>null</c> when access was not approved.</param>
/// <param name="Error">The OAuth <c>error</c> parameter, when present.</param>
/// <param name="ErrorDescription">The OAuth <c>error_description</c> parameter, when present.</param>
public sealed record SignInCallback(string? Code, string? Error, string? ErrorDescription)
{
    public bool Approved => !string.IsNullOrEmpty(Code);
}
