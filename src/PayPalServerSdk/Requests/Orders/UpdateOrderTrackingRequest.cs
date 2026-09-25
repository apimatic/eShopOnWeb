using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Requests.Orders;

/// <summary>
/// The inputs of the UpdateOrderTracking operation.
/// </summary>
public sealed record UpdateOrderTrackingRequest
{
    /// <summary>
    /// The ID of the order that the tracking information is associated with.
    /// </summary>
    [StringLength(36, MinimumLength = 1)]
    [RegularExpression("^[A-Z0-9]+$")]
    public required string Id { get; init; }

    /// <summary>
    /// The order tracking ID.
    /// </summary>
    [StringLength(36, MinimumLength = 1)]
    [RegularExpression("^[A-Z0-9]+$")]
    public required string TrackerId { get; init; }

    /// <summary>
    /// An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion.
    /// </summary>
    public string? PayPalAuthAssertion { get; init; }

    [MinLength(0)]
    [MaxLength(32767)]
    public IReadOnlyList<Patch>? Body { get; init; }
}
