using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- Request DTOs ----

/// <summary>Card details for a one-off payment or to be vaulted. Never stored or logged in full.</summary>
public record CardDto(
    string Number,
    string Expiry,          // YYYY-MM
    string SecurityCode,
    string? CardHolderName,
    BillingAddressDto? BillingAddress);

public record BillingAddressDto(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode,
    string? CountryCode);

public record OrderLineDto(int CatalogItemId, int Quantity);

public record ShippingAddressDto(string Street, string City, string State, string Country, string ZipCode);

public record CreateOrderRequest(List<OrderLineDto> Items, ShippingAddressDto? ShipTo);

/// <summary>Pay request body: supply either card details for a one-off payment, or a saved card id.</summary>
public record PayOrderBody(CardDto? Card, int? SavedPaymentMethodId);

/// <summary>Composed pay request: order id (from route) plus the funding source (from body).</summary>
public record PayOrderRequest(int OrderId, CardDto? Card, int? SavedPaymentMethodId);

/// <summary>Refund request body: optional partial amount (full when omitted) and a required idempotency key.</summary>
public record RefundBody(decimal? Amount, string IdempotencyKey);

/// <summary>Composed refund request: order id (from route) plus the refund body.</summary>
public record RefundRequest(int OrderId, decimal? Amount, string IdempotencyKey);

public record CreatePaymentMethodRequest(CardDto Card);

/// <summary>Reconciliation query: ISO-8601 date-time range.</summary>
public record ReconciliationRequest(DateTimeOffset From, DateTimeOffset To);

// ---- Response DTOs ----

public record CreateOrderResponse(int OrderId, string Status, decimal Total, string Currency);

public record RefundLineDto(string RefundId, decimal Amount, string Status, DateTimeOffset CreatedAt);

public record PaymentDto(
    string Currency,
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal AuthorizedAmount,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal TotalRefunded,
    decimal RefundableRemaining,
    IReadOnlyList<RefundLineDto> Refunds);

public record OrderResponse(
    int OrderId,
    string Status,
    decimal Total,
    string Currency,
    DateTimeOffset OrderDate,
    IReadOnlyList<OrderItemDto> Items,
    PaymentDto? Payment);

public record OrderItemDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record RefundResponse(string RefundId, string Status, decimal Amount, int OrderId, string OrderStatus);

public record PaymentMethodResponse(
    int PaymentMethodId,
    string? Brand,
    string? LastFourDigits,
    string? Expiry,
    string? CardHolderName,
    DateTimeOffset CreatedAt);

public record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopCapturedOrderCount,
    bool RangeEmpty,
    string Note,
    IReadOnlyList<MatchedTransactionDto> Matched,
    IReadOnlyList<PayPalTransactionDto> InPayPalNotInEShop,
    IReadOnlyList<UnmatchedOrderDto> InEShopNotInPayPal);

public record MatchedTransactionDto(
    int OrderId, string? PayPalTransactionId, string? PayPalStatus, decimal? PayPalAmount,
    string? Currency, decimal EShopCapturedAmount, string EShopStatus);

public record PayPalTransactionDto(
    string? TransactionId, string? Status, decimal? Amount, decimal? FeeAmount, string? Currency,
    string? InvoiceId, string? CustomId, DateTimeOffset? InitiatedAt, string? EventCode);

public record UnmatchedOrderDto(int OrderId, decimal CapturedAmount, string Currency, string Status, string? CaptureId);

/// <summary>Maps domain entities and gateway DTOs onto the API response shapes above.</summary>
public static class PaymentApiMapper
{
    public static CardDetails ToCardDetails(this CardDto dto) => new(
        dto.Number,
        dto.Expiry,
        dto.SecurityCode,
        dto.CardHolderName,
        dto.BillingAddress is null
            ? null
            : new CardBillingAddress(
                dto.BillingAddress.AddressLine1,
                dto.BillingAddress.AddressLine2,
                dto.BillingAddress.AdminArea1,
                dto.BillingAddress.AdminArea2,
                dto.BillingAddress.PostalCode,
                dto.BillingAddress.CountryCode));

    public static OrderResponse ToResponse(this Order order)
    {
        var payment = order.Payment is null ? null : ToPaymentDto(order.Payment);
        var items = order.OrderItems
            .Select(i => new OrderItemDto(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList();
        return new OrderResponse(
            order.Id, order.Status.ToString(), order.Total(),
            payment?.Currency ?? string.Empty, order.OrderDate, items, payment);
    }

    private static PaymentDto ToPaymentDto(OrderPayment p) => new(
        p.Currency,
        p.PayPalOrderId,
        p.AuthorizationId,
        p.AuthorizationStatus,
        p.AuthorizedAmount,
        p.AuthorizationExpiresAt,
        p.CaptureId,
        p.CaptureStatus,
        p.CapturedAmount,
        p.PayPalFee,
        p.NetAmount,
        p.TotalRefunded(),
        p.RefundableRemaining(),
        p.Refunds.Select(r => new RefundLineDto(r.PayPalRefundId, r.Amount, r.Status, r.CreatedAt)).ToList());

    public static PaymentMethodResponse ToResponse(this SavedPaymentMethod pm) => new(
        pm.Id, pm.Brand, pm.LastFourDigits, pm.Expiry, pm.CardHolderName, pm.CreatedAt);

    public static ReconciliationResponse ToResponse(this ReconciliationReport report)
    {
        var note = report.RangeEmpty
            ? "PayPal returned no transactions for this range. PayPal's transaction reporting lags live activity, " +
              "so a range covering payments you just created can legitimately be empty; use an older range that has settled data."
            : "PayPal transactions lined up against eShop orders for the range.";
        return new ReconciliationResponse(
            report.From, report.To, report.PayPalTransactionCount, report.EShopCapturedOrderCount, report.RangeEmpty, note,
            report.Matched.Select(m => new MatchedTransactionDto(
                m.OrderId, m.PayPalTransactionId, m.PayPalStatus, m.PayPalAmount, m.Currency, m.EShopCapturedAmount, m.EShopStatus.ToString())).ToList(),
            report.InPayPalNotInEShop.Select(t => new PayPalTransactionDto(
                t.TransactionId, t.Status, t.Amount, t.FeeAmount, t.Currency, t.InvoiceId, t.CustomId, t.InitiatedAt, t.EventCode)).ToList(),
            report.InEShopNotInPayPal.Select(o => new UnmatchedOrderDto(
                o.OrderId, o.CapturedAmount, o.Currency, o.Status.ToString(), o.CaptureId)).ToList());
    }
}
