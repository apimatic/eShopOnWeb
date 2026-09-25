using System.ComponentModel.DataAnnotations;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Requests.Orders;

/// <summary>
/// The inputs of the CaptureOrder operation.
/// </summary>
public sealed record CaptureOrderRequest
{
    /// <summary>
    /// The ID of the order for which to capture a payment.
    /// </summary>
    [StringLength(36, MinimumLength = 1)]
    [RegularExpression("^[A-Z0-9]+$")]
    public required string Id { get; init; }

    /// <summary>
    /// PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant.
    /// </summary>
    public string? PayPalMockResponse { get; init; }

    /// <summary>
    /// The server stores keys for 6 hours. The API callers can request the times to up to 72 hours by speaking to their Account Manager. It is mandatory for all single-step create order calls (E.g. Create Order Request with payment source information like Card, PayPal.vault_id, PayPal.billing_agreement_id, etc).
    /// </summary>
    [StringLength(108, MinimumLength = 1)]
    public string? PayPalRequestId { get; init; }

    /// <summary>
    /// The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.
    /// </summary>
    [StringLength(25, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z=,-]*$")]
    public string Prefer { get; init; } = "return=minimal";

    [StringLength(36, MinimumLength = 1)]
    public string? PayPalClientMetadataId { get; init; }

    /// <summary>
    /// An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion.
    /// </summary>
    public string? PayPalAuthAssertion { get; init; }

    public OrderCaptureRequest? Body { get; init; }
}
