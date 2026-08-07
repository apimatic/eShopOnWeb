namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Outcome of a completed PayPal card charge (create order + capture).</summary>
public sealed record PaymentCaptureResult(string PayPalOrderId, string CaptureId, string CaptureStatus)
{
    /// <summary>True when PayPal reports the capture as COMPLETED.</summary>
    public bool IsCompleted => string.Equals(CaptureStatus, "COMPLETED", System.StringComparison.OrdinalIgnoreCase);
}

/// <summary>A card that was stored in PayPal's vault, described safely (no PAN).</summary>
public sealed record VaultedCardResult(string VaultId, string Brand, string Last4, string? Expiry);

/// <summary>Outcome of a full refund of a captured payment.</summary>
public sealed record RefundResult(string RefundId, string RefundStatus)
{
    /// <summary>True when PayPal reports the refund as COMPLETED.</summary>
    public bool IsCompleted => string.Equals(RefundStatus, "COMPLETED", System.StringComparison.OrdinalIgnoreCase);
}
