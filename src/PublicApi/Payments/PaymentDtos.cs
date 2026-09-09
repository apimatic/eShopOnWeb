using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Payment/fulfilment view of an order returned to callers. Never contains card details.</summary>
public class OrderPaymentDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedGross { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RefundableRemaining { get; set; }
    public string? FailureReason { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static OrderPaymentDto From(OrderPayment p) => new()
    {
        OrderId = p.OrderId,
        Status = p.Status.ToString(),
        Currency = p.Currency,
        Amount = p.Amount,
        PayPalOrderId = p.PayPalOrderId,
        AuthorizationId = p.AuthorizationId,
        AuthorizationStatus = p.AuthorizationStatus,
        CaptureId = p.CaptureId,
        CaptureStatus = p.CaptureStatus,
        CapturedGross = p.CapturedGross,
        PayPalFee = p.PayPalFee,
        NetAmount = p.NetAmount,
        RefundedAmount = p.RefundedAmount,
        RefundableRemaining = p.RefundableRemaining,
        FailureReason = p.FailureReason,
        Refunds = p.Refunds
            .Select(r => new RefundDto { RefundId = r.RefundId, Amount = r.Amount, Status = r.Status })
            .ToList()
    };
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>Safe descriptor of a saved card — never full card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? Alias { get; set; }

    public static PaymentMethodDto From(SavedCard c) => new()
    {
        PaymentMethodId = c.Id,
        Brand = c.Brand,
        Last4 = c.Last4,
        Expiry = c.Expiry,
        Alias = c.Alias
    };
}

/// <summary>Card input accepted by the pay and save-card endpoints. Never stored or logged.</summary>
public class CardInput
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;   // YYYY-MM
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressInput? BillingAddress { get; set; }
}

public class BillingAddressInput
{
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}
