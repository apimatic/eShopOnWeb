using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

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
    public string CountryCode { get; init; } = "US";
}

public class PaymentHold
{
    public string PayPalOrderId { get; init; } = string.Empty;
    public string AuthorizationId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public class PaymentAuthorizationDetails
{
    public string AuthorizationId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public class PaymentCapture
{
    public string CaptureId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal CapturedAmount { get; init; }
    public decimal? PaypalFee { get; init; }
    public decimal? NetAmount { get; init; }
}

public class PaymentRefund
{
    public string RefundId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
}

public class VaultedCard
{
    public string VaultId { get; init; } = string.Empty;
    public string LastDigits { get; init; } = string.Empty;
    public string Brand { get; init; } = string.Empty;
    public string Expiry { get; init; } = string.Empty;
    public string? Name { get; init; }
}

public class PayPalReportedTransaction
{
    public string TransactionId { get; init; } = string.Empty;
    public string? ReferenceId { get; init; }
    public string? EventCode { get; init; }
    public string? Status { get; init; }
    public string? Currency { get; init; }
    public decimal? Amount { get; init; }
    public decimal? FeeAmount { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}
