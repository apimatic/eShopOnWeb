using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Card details for a one-off payment or for vaulting. Never persisted, never logged.
/// </summary>
public class CardDetails
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // ISO-8601 YYYY-MM
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardBillingAddress? BillingAddress { get; set; }
}

public class CardBillingAddress
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}

/// <summary>
/// Where the money for an order comes from: either full card details or a vaulted card.
/// </summary>
public class PayPalPaymentSource
{
    public CardDetails? Card { get; set; }
    public string? VaultTokenId { get; set; }
}

public class PayPalOrderInfo
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public PayPalAuthorizationInfo? Authorization { get; set; }
}

public class PayPalAuthorizationInfo
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ExpirationTime { get; set; }
}

public class PayPalCaptureInfo
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public bool FinalCapture { get; set; }
}

public class PayPalRefundInfo
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class PayPalVaultedCard
{
    public string VaultTokenId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class PayPalTransactionInfo
{
    public string? TransactionId { get; set; }
    public string? ReferenceId { get; set; }
    public string? ReferenceIdType { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public DateTimeOffset? InitiationTime { get; set; }
    public DateTimeOffset? UpdatedTime { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
}
