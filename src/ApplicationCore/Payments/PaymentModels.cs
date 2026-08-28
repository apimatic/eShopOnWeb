using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record CardBillingAddress(string CountryCode, string? AddressLine1, string? AddressLine2,
    string? City, string? State, string? PostalCode);

public sealed record CardDetails(string Name, string Number, string Expiry, string SecurityCode,
    CardBillingAddress BillingAddress);

public sealed record PaymentSource(CardDetails? Card, string? VaultId)
{
    public static PaymentSource FromCard(CardDetails card) => new(card, null);
    public static PaymentSource FromVault(string vaultId) => new(null, vaultId);
}

public sealed record PayPalOrderResult(string Id, string Status);
public sealed record AuthorizationResult(string Id, string Status, decimal Amount, string Currency,
    DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, string? CaptureId = null);
public sealed record CaptureResult(string Id, string Status, decimal Amount, string Currency,
    decimal? PayPalFee, decimal? NetAmount, DateTimeOffset? CreatedAt);
public sealed record RefundResult(string Id, string Status, decimal Amount, string Currency);
public sealed record VaultedCardResult(string Id, string? CustomerId, string Brand, string Last4, string Expiry);
public sealed record PayPalTransaction(string TransactionId, string? ReferenceId, string? InvoiceId,
    string? CustomId, DateTimeOffset? InitiatedAt, DateTimeOffset? UpdatedAt, decimal? Amount,
    string? Currency, decimal? Fee, string? Status, string? EventCode);

public sealed record CreateOrderItem(int CatalogItemId, int Quantity);
public sealed record ShippingAddress(string Street, string City, string State, string Country, string ZipCode);
public sealed record SavedCardView(int PaymentMethodId, string Brand, string Last4, string Expiry,
    DateTimeOffset CreatedAt);
public sealed record RefundView(int RefundId, string PayPalRefundId, string Status, decimal Amount,
    DateTimeOffset CreatedAt);
public sealed record OrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record OrderPaymentView(int OrderId, DateTimeOffset OrderDate, string BuyerId, decimal Total,
    string Currency, string PaymentStatus, string FulfilmentStatus, string? PayPalOrderId,
    string? AuthorizationId, string? AuthorizationStatus, DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId, string? CaptureStatus, decimal? CapturedAmount, decimal? PayPalFee,
    decimal? NetProceeds, decimal RefundedAmount, IReadOnlyList<OrderItemView> Items,
    IReadOnlyList<RefundView> Refunds);

public sealed record ReconciliationLine(string MatchStatus, int? OrderId, string? EShopRecordType,
    string? EShopPayPalId, string? PayPalTransactionId, string? PayPalReferenceId, string? InvoiceId,
    DateTimeOffset? PayPalInitiatedAt, decimal? Amount, string? Currency, decimal? Fee, string? PayPalStatus);

public sealed record ReconciliationReport(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationLine> Lines);
