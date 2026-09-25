namespace PayPalServerSdk.Requests.Payments;

/// <summary>
/// The inputs of the GetCapturedPayment operation.
/// </summary>
public sealed record GetCapturedPaymentRequest
{
    /// <summary>
    /// The PayPal-generated ID for the captured payment for which to show details.
    /// </summary>
    public required string CaptureId { get; init; }

    /// <summary>
    /// PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant.
    /// </summary>
    public string? PayPalMockResponse { get; init; }
}
