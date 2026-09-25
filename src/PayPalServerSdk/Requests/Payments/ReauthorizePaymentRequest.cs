using PayPalServerSdk.Models;

namespace PayPalServerSdk.Requests.Payments;

/// <summary>
/// The inputs of the ReauthorizePayment operation.
/// </summary>
public sealed record ReauthorizePaymentRequest
{
    /// <summary>
    /// The PayPal-generated ID for the authorized payment to reauthorize.
    /// </summary>
    public required string AuthorizationId { get; init; }

    /// <summary>
    /// The server stores keys for 45 days.
    /// </summary>
    public string? PayPalRequestId { get; init; }

    /// <summary>
    /// The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.
    /// </summary>
    public string Prefer { get; init; } = "return=minimal";

    /// <summary>
    /// An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see <see href="/docs/api/reference/api-requests/#paypal-auth-assertion">PayPal-Auth-Assertion</see>. Note:For three party transactions in which a partner is managing the API calls on behalf of a merchant, the partner must identify the merchant using either a PayPal-Auth-Assertion header or an access token with target_subject.
    /// </summary>
    public string? PayPalAuthAssertion { get; init; }

    public ReauthorizeRequest? Body { get; init; }
}
