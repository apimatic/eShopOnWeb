using System.ComponentModel.DataAnnotations;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Requests.Orders;

/// <summary>
/// The inputs of the CreateOrderTracking operation.
/// </summary>
public sealed record CreateOrderTrackingRequest
{
    /// <summary>
    /// The ID of the order that the tracking information is associated with.
    /// </summary>
    [StringLength(36, MinimumLength = 1)]
    [RegularExpression("^[A-Z0-9]+$")]
    public required string Id { get; init; }

    /// <summary>
    /// An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion.
    /// </summary>
    public string? PayPalAuthAssertion { get; init; }

    public required OrderTrackerRequest Body { get; init; }
}
