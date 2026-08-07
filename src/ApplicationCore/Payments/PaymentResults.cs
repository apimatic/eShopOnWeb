namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Result of a successful PayPal charge (order created and captured).</summary>
public class GatewayChargeResult
{
    public GatewayChargeResult(string gatewayOrderId, string captureId, string status)
    {
        GatewayOrderId = gatewayOrderId;
        CaptureId = captureId;
        Status = status;
    }

    /// <summary>PayPal Orders v2 order id.</summary>
    public string GatewayOrderId { get; }

    /// <summary>PayPal capture id — the handle required to later refund the payment.</summary>
    public string CaptureId { get; }

    public string Status { get; }
}

/// <summary>Result of a successful full refund of a capture.</summary>
public class GatewayRefundResult
{
    public GatewayRefundResult(string refundId, string status)
    {
        RefundId = refundId;
        Status = status;
    }

    public string RefundId { get; }
    public string Status { get; }
}

/// <summary>Result of vaulting a card. Contains only safe, display-friendly descriptors plus the token.</summary>
public class GatewaySavedCard
{
    public GatewaySavedCard(string vaultToken, string last4, string brand, string expiry, string? customerId)
    {
        VaultToken = vaultToken;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
        CustomerId = customerId;
    }

    /// <summary>PayPal Vault payment-token id.</summary>
    public string VaultToken { get; }
    public string Last4 { get; }
    public string Brand { get; }
    public string Expiry { get; }

    /// <summary>PayPal Vault customer id this token belongs to (may be gateway-generated).</summary>
    public string? CustomerId { get; }
}
