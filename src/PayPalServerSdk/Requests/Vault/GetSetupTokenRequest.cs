using System.ComponentModel.DataAnnotations;

namespace PayPalServerSdk.Requests.Vault;

/// <summary>
/// The inputs of the GetSetupToken operation.
/// </summary>
public sealed record GetSetupTokenRequest
{
    /// <summary>
    /// ID of the setup token.
    /// </summary>
    [StringLength(36, MinimumLength = 7)]
    [RegularExpression("^[0-9a-zA-Z_-]+$")]
    public required string Id { get; init; }
}
