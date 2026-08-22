using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class CardPaymentSource
{
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public string? SecurityCode { get; init; }
    public string? Name { get; init; }
    public CardBillingAddress? BillingAddress { get; init; }
}

public class CardBillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; }
}

public class PayPalPurchaseLine
{
    public required string Name { get; init; }
    public required string Sku { get; init; }
    public required string Description { get; init; }
    public required decimal UnitAmount { get; init; }
    public required int Quantity { get; init; }
}

public class PayPalAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
    public string? CardLastDigits { get; init; }
    public string? CardBrand { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}

public class PayPalCaptureResult
{
    public required string CaptureId { get; init; }
    public required string Status { get; init; }
    public required decimal CapturedAmount { get; init; }
    public required decimal PaypalFee { get; init; }
    public required decimal NetAmount { get; init; }
    public required string Currency { get; init; }
}

public class PayPalRefundResult
{
    public required string RefundId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}

public class PayPalVaultedCard
{
    public required string PaymentTokenId { get; init; }
    public required string CustomerId { get; init; }
    public string? LastDigits { get; init; }
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

public class PayPalReportedTransaction
{
    public required string TransactionId { get; init; }
    public string? PaypalReferenceId { get; init; }
    public string? InvoiceId { get; init; }
    public string? EventCode { get; init; }
    public string? Status { get; init; }
    public decimal? Amount { get; init; }
    public decimal? Fee { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}

public class PayPalAuthorizationDetails
{
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
}
