using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class PayPalAuthorizeResult
{
    public string PayPalOrderId { get; init; } = string.Empty;
    public string PayPalOrderStatus { get; init; } = string.Empty;
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? AuthorizationExpiresAt { get; init; }
    public decimal AuthorizedAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public class PayPalAuthorizationDetails
{
    public string AuthorizationId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset? ExpirationTime { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public class PayPalCaptureResult
{
    public string CaptureId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal CapturedAmount { get; init; }
    public decimal PayPalFee { get; init; }
    public decimal NetAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public class PayPalRefundResult
{
    public string PayPalRefundId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public class PayPalVaultedCard
{
    public string PaymentTokenId { get; init; } = string.Empty;
    public string? PayPalCustomerId { get; init; }
    public string Last4 { get; init; } = string.Empty;
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

public class PayPalReportedTransaction
{
    public string TransactionId { get; init; } = string.Empty;
    public string? PayPalReferenceId { get; init; }
    public string? PayPalReferenceIdType { get; init; }
    public string? EventCode { get; init; }
    public string? Status { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public decimal? Amount { get; init; }
    public decimal? FeeAmount { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}

public class PayPalLineItem
{
    public string Name { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}
