namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>Outcome of creating + capturing a PayPal order.</summary>
public class PayPalCaptureResult
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string CaptureId { get; set; } = string.Empty;

    /// <summary>PayPal capture status, e.g. COMPLETED.</summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>Outcome of vaulting a card, including the safe descriptor to show the shopper.</summary>
public class VaultedCardResult
{
    public string VaultId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
}

/// <summary>Outcome of refunding a captured payment.</summary>
public class RefundResult
{
    public string RefundId { get; set; } = string.Empty;

    /// <summary>PayPal refund status, e.g. COMPLETED.</summary>
    public string Status { get; set; } = string.Empty;
}
