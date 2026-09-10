using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- Request shapes ----

public record ShipToAddressDto(string Street, string City, string State, string Country, string ZipCode);
public record PlaceOrderItemDto(int CatalogItemId, int Quantity);
public record PlaceOrderRequest(List<PlaceOrderItemDto> Items, ShipToAddressDto? ShipToAddress);

public record CardDto(string Number, string Expiry, string SecurityCode, string? CardholderName);
public record BillingAddressDto(string? AddressLine1, string? AdminArea2, string? AdminArea1,
    string? PostalCode, string? CountryCode);

/// <summary>Pay a specific order: supply either <see cref="Card"/> or a saved <see cref="PaymentMethodId"/>.</summary>
public record PayOrderRequest(CardDto? Card, string? PaymentMethodId, BillingAddressDto? BillingAddress);

public record RefundRequestDto(decimal? Amount, string IdempotencyKey);

public record SavePaymentMethodRequest(CardDto Card, BillingAddressDto? BillingAddress);

// ---- Response shapes ----

public record RefundDto(string RefundId, decimal Amount, string Status, DateTimeOffset CreatedAt);

public record PaymentStateDto(
    int OrderId,
    string Status,
    decimal Amount,
    string Currency,
    string? PaymentMethod,
    string? ProcessorOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetProceeds,
    decimal TotalRefunded,
    decimal RefundableRemaining,
    IReadOnlyList<RefundDto> Refunds);

public record OrderLineDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record MyOrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderLineDto> Items,
    PaymentStateDto? Payment);

public record SavedCardDto(string PaymentMethodId, string Brand, string LastDigits, string? Expiry,
    string? CardholderName, DateTimeOffset CreatedAt);

public record ReconciliationLineDto(string Match, string? InvoiceReference, int? OrderId,
    string? PayPalTransactionId, string? PayPalStatus, decimal? PayPalAmount, string? Currency,
    decimal? EShopAmount, DateTimeOffset? TransactionDate);

public record ReconciliationReportDto(
    DateTimeOffset From, DateTimeOffset To,
    int PayPalTransactionCount, int EShopPaymentCount,
    int MatchedCount, int MissingInEShopCount, int MissingInPayPalCount,
    IReadOnlyList<ReconciliationLineDto> Lines);

/// <summary>Maps domain entities to the API response DTOs.</summary>
public static class PaymentMapper
{
    public static PaymentStateDto ToDto(OrderPayment p) => new(
        p.OrderId,
        p.Status.ToString(),
        p.Amount,
        p.CurrencyCode,
        p.PaymentMethodDescription,
        p.ProcessorOrderId,
        p.AuthorizationId,
        p.AuthorizationStatus,
        p.AuthorizationExpiresAt,
        p.CaptureId,
        p.CaptureStatus,
        p.CapturedAmount,
        p.PayPalFee,
        p.NetAmount,
        p.TotalRefunded(),
        p.RefundableRemaining(),
        p.Refunds.OrderBy(r => r.CreatedAt)
            .Select(r => new RefundDto(r.RefundId, r.Amount, r.Status, r.CreatedAt)).ToList());

    public static MyOrderDto ToDto(Order order, OrderPayment? payment) => new(
        order.Id,
        order.OrderDate,
        order.Total(),
        order.OrderItems.Select(i => new OrderLineDto(
            i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList(),
        payment is null ? null : ToDto(payment));

    public static SavedCardDto ToDto(SavedPaymentMethod m) =>
        new(m.PaymentMethodId, m.Brand, m.LastDigits, m.Expiry, m.CardholderName, m.CreatedAt);

    public static ReconciliationReportDto ToDto(ReconciliationReport r) => new(
        r.From, r.To, r.PayPalTransactionCount, r.EShopPaymentCount,
        r.MatchedCount, r.MissingInEShopCount, r.MissingInPayPalCount,
        r.Lines.Select(l => new ReconciliationLineDto(
            l.Match.ToString(), l.InvoiceReference, l.OrderId, l.PayPalTransactionId, l.PayPalStatus,
            l.PayPalAmount, l.CurrencyCode, l.EShopAmount, l.TransactionDate)).ToList());

    public static CardDetails ToDomain(CardDto c) =>
        new(c.Number, c.Expiry, c.SecurityCode, c.CardholderName);

    public static BillingAddress? ToDomain(BillingAddressDto? a) => a is null
        ? null
        : new BillingAddress(a.AddressLine1, a.AdminArea2, a.AdminArea1, a.PostalCode, a.CountryCode);
}
