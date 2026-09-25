using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Requests.Orders;

/// <summary>
/// The inputs of the PatchOrder operation.
/// </summary>
public sealed record PatchOrderRequest
{
    /// <summary>
    /// The ID of the order to update.
    /// </summary>
    [StringLength(36, MinimumLength = 1)]
    [RegularExpression("^[A-Z0-9]+$")]
    public required string Id { get; init; }

    /// <summary>
    /// PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant.
    /// </summary>
    public string? PayPalMockResponse { get; init; }

    /// <summary>
    /// An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion.
    /// </summary>
    public string? PayPalAuthAssertion { get; init; }

    [MinLength(0)]
    [MaxLength(32767)]
    public IReadOnlyList<Patch>? Body { get; init; }
}
