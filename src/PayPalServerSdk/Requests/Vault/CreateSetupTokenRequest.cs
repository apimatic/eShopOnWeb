using System.ComponentModel.DataAnnotations;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Requests.Vault;

/// <summary>
/// The inputs of the CreateSetupToken operation.
/// </summary>
public sealed record CreateSetupTokenRequest
{
    /// <summary>
    /// The server stores keys for 3 hours.
    /// </summary>
    [StringLength(108, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? PayPalRequestId { get; init; }

    /// <summary>
    /// Setup Token creation with a instrument type optional financial instrument details and customer_id.
    /// </summary>
    public required SetupTokenRequest Body { get; init; }
}
