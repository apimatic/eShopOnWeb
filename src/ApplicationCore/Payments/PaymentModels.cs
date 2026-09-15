using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A catalog item and quantity requested when placing an order.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>Optional ship-to address supplied when placing an order.</summary>
public record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>
/// How to pay an order: either raw card details for a one-off payment, or one of the shopper's saved cards.
/// Exactly one is provided.
/// </summary>
public record PayInstruction(PayPalRawCard? Card, int? SavedPaymentMethodId);

/// <summary>A read model of a payment's full state, returned by the payment/order endpoints.</summary>
public record PaymentView(
    int OrderId,
    string BuyerId,
    string Status,
    string CurrencyCode,
    decimal Amount,
    DateTimeOffset CreatedAt,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal TotalRefunded,
    int? SavedPaymentMethodId,
    IReadOnlyList<RefundView> Refunds)
{
    public static PaymentView From(Payment p)
    {
        var refunds = new List<RefundView>();
        foreach (var r in p.Refunds)
        {
            refunds.Add(new RefundView(r.Id, r.Status, r.Amount, r.PayPalRefundId, r.CreatedAt));
        }

        return new PaymentView(
            p.OrderId, p.BuyerId, p.Status.ToString(), p.CurrencyCode, p.Amount, p.CreatedAt,
            p.AuthorizationId, p.AuthorizationStatus, p.AuthorizationExpiresAt,
            p.CaptureId, p.CaptureStatus, p.CapturedAmount, p.PayPalFee, p.NetAmount,
            p.TotalRefunded, p.SavedPaymentMethodId, refunds);
    }
}

public record RefundView(Guid RefundId, string Status, decimal Amount, string? PayPalRefundId, DateTimeOffset CreatedAt);

/// <summary>The outcome of a refund request, including running totals against the capture.</summary>
public record RefundReceipt(
    Guid RefundId,
    string Status,
    decimal Amount,
    string? PayPalRefundId,
    decimal CapturedAmount,
    decimal TotalRefunded,
    decimal RefundableRemaining);

/// <summary>A reconciliation line joining PayPal's record and eShop's record for one transaction/order.</summary>
public record ReconciliationEntry(
    string Match,                 // MATCHED | PAYPAL_ONLY | ESHOP_ONLY
    int? OrderId,
    string? CustomId,
    string? PayPalTransactionId,
    string? PayPalEventCode,
    string? PayPalStatus,
    decimal? PayPalAmount,
    decimal? PayPalFee,
    string? CurrencyCode,
    DateTimeOffset? PayPalDate,
    string? EShopStatus,
    decimal? EShopCapturedAmount);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopPaymentCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
