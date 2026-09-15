using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- Requests ----

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public AddressDto? ShipToAddress { get; set; }
}

/// <summary>Raw card input. Only ever forwarded to PayPal; never stored or logged by this app.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;   // "YYYY-MM"
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class PayOrderRequest
{
    /// <summary>A one-off card. Mutually exclusive with <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Pay with one of the shopper's saved cards instead of a one-off card.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class RefundRequest
{
    /// <summary>Amount to refund; omit for a full refund of the remaining refundable amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeats under the same key never refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

// ---- Responses ----

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentStateDto
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string PaymentReference { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string AuthorizationStatus { get; set; } = string.Empty;
    public decimal AuthorizedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? SavedCard { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedTotal { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public PaymentStateDto? Payment { get; set; }
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

public class MyOrdersResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
}

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class SaveCardResponse
{
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
}

public class PaymentMethodsResponse
{
    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int PayPalOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationMatch> Matched { get; set; } = new();
    public List<PayPalOnlyEntry> PayPalOnly { get; set; } = new();
    public List<EShopOnlyEntry> EShopOnly { get; set; } = new();
}

// ---- Mapping ----

public static class PaymentApiMapper
{
    public static CardDetails ToCardDetails(CardDto card) => new(
        card.Number,
        card.Expiry,
        card.SecurityCode,
        card.CardholderName,
        card.BillingAddressLine1,
        card.BillingAddressLine2,
        card.City,
        card.State,
        card.PostalCode,
        string.IsNullOrWhiteSpace(card.CountryCode) ? "US" : card.CountryCode!);

    public static PaymentStateDto? ToPaymentStateDto(OrderPayment? payment)
    {
        if (payment is null)
        {
            return null;
        }

        return new PaymentStateDto
        {
            PayPalOrderId = payment.PayPalOrderId,
            PaymentReference = payment.PaymentReference,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizedAmount = payment.AuthorizedAmount,
            Currency = payment.Currency,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            SavedCard = payment.SavedCardDescriptor,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            RefundedTotal = payment.TotalRefunded(),
            RemainingRefundable = payment.RemainingRefundable(),
            Refunds = payment.Refunds
                .Select(r => new RefundDto
                {
                    RefundId = r.PayPalRefundId,
                    Amount = r.Amount,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt
                })
                .ToList()
        };
    }

    public static OrderSummaryDto ToOrderSummaryDto(Order order) => new()
    {
        OrderId = order.Id,
        Status = order.Status.ToString(),
        Total = order.Total(),
        OrderDate = order.OrderDate,
        Payment = ToPaymentStateDto(order.Payment)
    };

    public static SavedCardDto ToSavedCardDto(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        CardBrand = method.CardBrand,
        LastFourDigits = method.LastFourDigits,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName,
        CreatedAt = method.CreatedAt
    };

    public static ReconciliationResponse ToReconciliationResponse(ReconciliationReport report) => new()
    {
        From = report.From,
        To = report.To,
        MatchedCount = report.Matched.Count,
        PayPalOnlyCount = report.PayPalOnly.Count,
        EShopOnlyCount = report.EShopOnly.Count,
        Matched = report.Matched.ToList(),
        PayPalOnly = report.PayPalOnly.ToList(),
        EShopOnly = report.EShopOnly.ToList()
    };
}
