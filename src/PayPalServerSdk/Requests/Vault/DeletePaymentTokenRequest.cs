using System.ComponentModel.DataAnnotations;

namespace PayPalServerSdk.Requests.Vault;

/// <summary>
/// The inputs of the DeletePaymentToken operation.
/// </summary>
public sealed record DeletePaymentTokenRequest
{
    /// <summary>
    /// ID of the payment token.
    /// </summary>
    [StringLength(36, MinimumLength = 1)]
    [RegularExpression("^[0-9a-zA-Z_-]+$")]
    public required string Id { get; init; }
}
