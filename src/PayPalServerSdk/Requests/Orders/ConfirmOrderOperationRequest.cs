using System.ComponentModel.DataAnnotations;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Requests.Orders;

/// <summary>
/// The inputs of the ConfirmOrder operation.
/// </summary>
public sealed record ConfirmOrderOperationRequest
{
    /// <summary>
    /// The ID of the order for which the payer confirms their intent to pay.
    /// </summary>
    [StringLength(36, MinimumLength = 1)]
    [RegularExpression("^[A-Z0-9]+$")]
    public required string Id { get; init; }

    [StringLength(36, MinimumLength = 1)]
    public string? PayPalClientMetadataId { get; init; }

    /// <summary>
    /// An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion.
    /// </summary>
    public string? PayPalAuthAssertion { get; init; }

    /// <summary>
    /// The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.
    /// </summary>
    [StringLength(25, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z=]*$")]
    public string Prefer { get; init; } = "return=minimal";

    public ConfirmOrderRequest? Body { get; init; }
}
