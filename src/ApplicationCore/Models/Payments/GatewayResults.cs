using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

public class GatewayAuthorizationResult
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
}

public class GatewayAuthorizationDetails
{
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
}

public class GatewayCaptureResult
{
    public string CaptureId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal PayPalFee { get; set; }
    public decimal NetAmount { get; set; }
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
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class GatewayTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal? Fee { get; set; }
    public DateTimeOffset? Time { get; set; }
    public string? CustomField { get; set; }
}

public class GatewayTransactionPage
{
    public IReadOnlyList<GatewayTransaction> Transactions { get; set; } = Array.Empty<GatewayTransaction>();
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
}
