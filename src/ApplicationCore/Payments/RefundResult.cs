namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>The outcome of a PayPal refund: the refund id and its status.</summary>
public sealed record RefundResult(string RefundId, string Status)
{
    /// <summary>PayPal reports a settled refund as <c>COMPLETED</c>; <c>PENDING</c> also succeeds asynchronously.</summary>
    public bool IsSuccessful =>
        string.Equals(Status, "COMPLETED", System.StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Status, "PENDING", System.StringComparison.OrdinalIgnoreCase);
}
