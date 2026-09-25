namespace PayPalServerSdk.Requests.Payments;

/// <summary>
/// The inputs of the GetRefund operation.
/// </summary>
public sealed record GetRefundRequest
{
    /// <summary>
    /// The PayPal-generated ID for the refund for which to show details.
    /// </summary>
    public required string RefundId { get; init; }

    /// <summary>
    /// PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant.
    /// </summary>
    public string? PayPalMockResponse { get; init; }

    /// <summary>
    /// An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see <see href="/docs/api/reference/api-requests/#paypal-auth-assertion">PayPal-Auth-Assertion</see>. Note:For three party transactions in which a partner is managing the API calls on behalf of a merchant, the partner must identify the merchant using either a PayPal-Auth-Assertion header or an access token with target_subject.
    /// </summary>
    public string? PayPalAuthAssertion { get; init; }
}
