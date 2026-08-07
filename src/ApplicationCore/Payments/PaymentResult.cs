namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// The outcome of a PayPal charge: the created Orders v2 order id and the capture id/status that a
/// refund is later issued against.
/// </summary>
public sealed record PaymentResult(
    string PayPalOrderId,
    string CaptureId,
    string CaptureStatus)
{
    /// <summary>A capture is money-in only when PayPal reports it <c>COMPLETED</c>.</summary>
    public bool IsCompleted =>
        string.Equals(CaptureStatus, "COMPLETED", System.StringComparison.OrdinalIgnoreCase);
}
