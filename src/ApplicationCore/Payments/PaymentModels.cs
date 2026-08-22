using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class CardDetails
{
    public string Number { get; init; } = string.Empty;
    public string Expiry { get; init; } = string.Empty;
    public string SecurityCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public CardBillingAddress? BillingAddress { get; init; }
}

public class CardBillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
    public string CountryCode { get; init; } = "US";
}

public class PayPalOrderResult
{
    public string Id { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool PayerActionRequired { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? AuthorizationExpiration { get; init; }
}

public class PayPalAuthorizationResult
{
    public string Id { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset? ExpirationTime { get; init; }
    public decimal? Amount { get; init; }
}

public class PayPalCaptureResult
{
    public string Id { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal CapturedAmount { get; init; }
    public decimal? PaypalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public string? Currency { get; init; }
}

public class PayPalRefundResult
{
    public string Id { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
}

public class VaultedCardResult
{
    public string VaultId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public string LastDigits { get; init; } = string.Empty;
    public string Brand { get; init; } = string.Empty;
    public string Expiry { get; init; } = string.Empty;
    public string? CardholderName { get; init; }
    public bool PayerActionRequired { get; init; }
}

public class PayPalReportedTransaction
{
    public string TransactionId { get; init; } = string.Empty;
    public string? ReferenceId { get; init; }
    public string? CustomField { get; init; }
    public string? InvoiceId { get; init; }
    public string? EventCode { get; init; }
    public string? Status { get; init; }
    public string? Amount { get; init; }
    public string? Currency { get; init; }
    public string? FeeAmount { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}

public class OrderLine
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public class ReconciliationRow
{
    public string MatchStatus { get; init; } = string.Empty;
    public int? OrderId { get; init; }
    public string? OrderPaymentStatus { get; init; }
    public string? PayPalTransactionId { get; init; }
    public string? PayPalReferenceId { get; init; }
    public string? PayPalCustomField { get; init; }
    public string? PayPalEventCode { get; init; }
    public string? PayPalStatus { get; init; }
    public string? Amount { get; init; }
    public string? Currency { get; init; }
    public string? Notes { get; init; }
}
