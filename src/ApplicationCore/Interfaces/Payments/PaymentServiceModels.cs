using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>Settings the payment flow needs from configuration (bound from the PayPal section).</summary>
public interface IPaymentSettings
{
    /// <summary>ISO-4217 currency code amounts are charged in (PayPal:Currency).</summary>
    string Currency { get; }
}

public record OrderLineInput(int CatalogItemId, int Quantity);

public record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>Instruction to authorize an order: exactly one of a one-off card or a saved card.</summary>
public record PayInstruction(PayPalCardDetails? Card, int? SavedPaymentMethodId, bool SaveCard);

public record PlaceOrderResult(int OrderId, decimal Total, string CurrencyCode, string Status);

public record RefundView(int RefundId, string PayPalRefundId, decimal Amount, string CurrencyCode, string Status, DateTimeOffset CreatedAt);

/// <summary>An order together with the payment state that follows it.</summary>
public record OrderPaymentView(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Amount,
    string CurrencyCode,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string? PaymentInstrumentDescription,
    decimal RefundableAmount,
    IReadOnlyList<RefundView> Refunds);

public record RefundResultView(
    int RefundId,
    string PayPalRefundId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    decimal RefundableRemaining);

public record SavedCardView(
    int PaymentMethodId,
    string Brand,
    string LastFourDigits,
    string Expiry,
    string CardholderName,
    DateTimeOffset CreatedAt);

public record SavedCardResult(int PaymentMethodId, SavedCardView Card);

/// <summary>A PayPal transaction lined up against the eShop order it belongs to (if any).</summary>
public record ReconciliationEntry(
    string TransactionId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    decimal? FeeAmount,
    DateTimeOffset? InitiationDate,
    string? InvoiceId,
    int? MatchedOrderId);

/// <summary>An eShop payment that moved money but for which PayPal reporting shows no transaction yet.</summary>
public record UnreconciledOrder(int OrderId, string InvoiceId, string? CaptureId, decimal? CapturedAmount, string CurrencyCode, string Status);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset GeneratedAt,
    int PayPalTransactionCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> InPayPalNotInEShop,
    IReadOnlyList<UnreconciledOrder> InEShopNotInPayPal,
    string Note);
