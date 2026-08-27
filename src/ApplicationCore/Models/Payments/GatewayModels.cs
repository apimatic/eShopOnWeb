using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>
/// Card details in transit only. Never persisted, never logged.
/// </summary>
public class CardDetails
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AdminArea2 { get; set; } // city
    public string? AdminArea1 { get; set; } // state
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

public class GatewayAuthorization
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public class GatewayCapture
{
    public string CaptureId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class GatewayRefundResult
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class GatewayVaultedCard
{
    public string VaultTokenId { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
}

public class GatewayTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? Time { get; set; }
}
