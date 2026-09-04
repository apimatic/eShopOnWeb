using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayPalBillingAddressRequest
{
    public string Line1 { get; set; } = "";
    public string? Line2 { get; set; }
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string CountryCode { get; set; } = "";
}

/// <summary>
/// Raw card details for a one-off payment or saving a card.
/// Bound from the request body only; never logged, never persisted.
/// </summary>
public class PayPalCardRequest
{
    public string Number { get; set; } = "";
    public string Expiry { get; set; } = "";
    public string Name { get; set; } = "";
    public PayPalBillingAddressRequest BillingAddress { get; set; } = new();
}

/// <summary>Payment state as reported by PayPal, safe to show.</summary>
public class OrderPaymentDto
{
    public int PaymentId { get; set; }
    public string Currency { get; set; } = "";
    public decimal AmountAuthorized { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public decimal CapturedAmount { get; set; }
    public decimal PayPalFee { get; set; }
    public decimal NetAmount { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Status { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
