using System.ComponentModel.DataAnnotations;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Requests.Vault;

/// <summary>
/// The inputs of the CreatePaymentToken operation.
/// </summary>
public sealed record CreatePaymentTokenRequest
{
    /// <summary>
    /// The server stores keys for 3 hours.
    /// </summary>
    [StringLength(108, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? PayPalRequestId { get; init; }

    /// <summary>
    /// Payment Token creation with a financial instrument and an optional customer_id.
    /// </summary>
    public required PaymentTokenRequest Body { get; init; }
}
