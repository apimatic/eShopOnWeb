namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Bound from the "Finix" configuration section. No credential values are
/// committed to the repository; they are supplied by the environment or by
/// .NET user-secrets.
/// </summary>
public class FinixSettings
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    /// <summary>How long a fulfilment/refund request may wait for a capture transfer to settle.</summary>
    public int CaptureSettlementTimeoutSeconds { get; set; } = 30;

    /// <summary>How long a partial refund may wait for a pending capture to settle.</summary>
    public int RefundSettlementTimeoutSeconds { get; set; } = 90;
}
