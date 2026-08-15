using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

public record OrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record OrderView(
    int OrderId,
    string Status,
    decimal Total,
    string Currency,
    DateTimeOffset OrderDate,
    IReadOnlyList<OrderItemView> Items);

public record RefundView(string RefundId, decimal Amount, string Currency, string Status, DateTimeOffset CreatedAt);

/// <summary>The payment state PayPal owns for an order — ids and statuses for the hold, capture and refunds.</summary>
public record PaymentView(
    string Status,
    decimal Amount,
    string Currency,
    string PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal TotalRefunded,
    decimal RefundableRemaining,
    IReadOnlyList<RefundView> Refunds);

/// <summary>An order together with its payment state.</summary>
public record OrderPaymentView(int OrderId, OrderView Order, PaymentView? Payment);

public static class PaymentResponseFactory
{
    public static OrderPaymentView From(OrderPaymentState state, string fallbackCurrency)
        => new(state.Order.Id, MapOrder(state.Order, state.Payment?.Currency ?? fallbackCurrency), MapPayment(state.Payment));

    public static OrderView MapOrder(Order order, string currency)
        => new(
            order.Id,
            order.Status.ToString(),
            order.Total(),
            currency,
            order.OrderDate,
            order.OrderItems.Select(i => new OrderItemView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList());

    public static PaymentView? MapPayment(Payment? payment)
    {
        if (payment is null) return null;
        return new PaymentView(
            payment.Status.ToString(),
            payment.Amount,
            payment.Currency,
            payment.PayPalOrderId,
            payment.AuthorizationId,
            payment.AuthorizationStatus,
            payment.CaptureId,
            payment.CaptureStatus,
            payment.CapturedAmount,
            payment.PayPalFee,
            payment.NetAmount,
            payment.TotalRefunded(),
            payment.RefundableRemaining(),
            payment.Refunds.Select(r => new RefundView(r.PayPalRefundId, r.Amount, r.Currency, r.Status, r.CreatedAt)).ToList());
    }
}
