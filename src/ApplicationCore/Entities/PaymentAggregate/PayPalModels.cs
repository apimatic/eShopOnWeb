using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class CardPaymentDetails
{
    public string Number { get; init; } = string.Empty;
    public string Expiry { get; init; } = string.Empty;
    public string? SecurityCode { get; init; }
    public string? Name { get; init; }
    public CardBillingAddress? BillingAddress { get; init; }
}

public class CardBillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; }
}

public class PayPalMoney
{
    public string CurrencyCode { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public class PayPalAuthorizationResult
{
    public string PayPalOrderId { get; init; } = string.Empty;
    public string AuthorizationId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public PayPalMoney Amount { get; init; } = new();
    public string? CardBrand { get; init; }
    public string? CardLast4 { get; init; }
}

public class PayPalAuthorizationDetails
{
    public string AuthorizationId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public PayPalMoney Amount { get; init; } = new();
}

public class PayPalCaptureResult
{
    public string CaptureId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal CapturedAmount { get; init; }
    public decimal? PaypalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public class PayPalRefundResult
{
    public string RefundId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public class PayPalVaultedCardResult
{
    public string PaymentTokenId { get; init; } = string.Empty;
    public string? CustomerId { get; init; }
    public string Brand { get; init; } = string.Empty;
    public string Last4 { get; init; } = string.Empty;
    public string Expiry { get; init; } = string.Empty;
    public string? CardholderName { get; init; }
}

public class PayPalReportedTransaction
{
    public string TransactionId { get; init; } = string.Empty;
    public string? PaypalReferenceId { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public string? EventCode { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public decimal? Fee { get; init; }
}
