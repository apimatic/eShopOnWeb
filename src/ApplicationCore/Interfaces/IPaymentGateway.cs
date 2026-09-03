using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record CardPaymentDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

public sealed record CardBillingAddress(
    string CountryCode,
    string? AddressLine1,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode);

public sealed record AuthorizationResult(
    string PayPalOrderId,
    string OrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? Expiration,
    DateTimeOffset? CreatedAt,
    decimal Amount);

public sealed record AuthorizationSnapshot(
    string AuthorizationId,
    string Status,
    DateTimeOffset? Expiration,
    DateTimeOffset? CreatedAt);

public sealed record CaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PaypalFee,
    decimal? NetAmount);

public sealed record RefundResult(
    string RefundId,
    string Status,
    decimal Amount);

public sealed record VaultedCard(
    string VaultId,
    string? PayPalCustomerId,
    string LastDigits,
    string? Brand,
    string? Expiry,
    string? CardholderName);

public sealed record ProviderTransaction(
    string TransactionId,
    string? ReferenceId,
    string? InvoiceId,
    string? CustomField,
    string? Amount,
    string? FeeAmount,
    string? Currency,
    string? Status,
    string? InitiationDate);

public interface IPaymentGateway
{
    Task<AuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currency,
        CardPaymentDetails card,
        string requestId,
        CancellationToken ct);

    Task<AuthorizationResult> AuthorizeVaultedCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string vaultId,
        string requestId,
        CancellationToken ct);

    Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    Task<AuthorizationSnapshot> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken ct);

    Task<CaptureResult> CaptureAsync(
        string authorizationId,
        string requestId,
        string? invoiceId,
        CancellationToken ct);

    Task VoidAsync(string authorizationId, string requestId, CancellationToken ct);

    Task<RefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken ct);

    Task<VaultedCard> VaultCardAsync(
        string merchantCustomerId,
        CardPaymentDetails card,
        string? requestId,
        CancellationToken ct);

    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);
}

public sealed record PlaceOrderItem(int CatalogItemId, int Quantity);

public sealed record PlaceOrderResult(int OrderId, decimal Total, string Currency, OrderPaymentStatus Status);

public sealed record PayOrderResult(
    int OrderId,
    OrderPaymentStatus Status,
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiration,
    decimal Amount,
    string Currency);

public sealed record FulfilOrderResult(
    int OrderId,
    OrderPaymentStatus Status,
    string CaptureId,
    string CaptureStatus,
    decimal CapturedAmount,
    decimal? PaypalFee,
    decimal? NetAmount,
    string Currency);

public sealed record CancelOrderResult(int OrderId, OrderPaymentStatus Status, string AuthorizationStatus);

public sealed record RefundOrderResult(
    string RefundId,
    int OrderId,
    OrderPaymentStatus Status,
    decimal RefundedAmount,
    decimal RemainingRefundable,
    string Currency);

public interface ICheckoutService
{
    Task<PlaceOrderResult> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderItem> items,
        Address shipTo,
        CancellationToken ct);

    Task<PayOrderResult> PayAsync(
        string buyerId,
        int orderId,
        CardPaymentDetails? card,
        int? paymentMethodId,
        CancellationToken ct);

    Task<FulfilOrderResult> FulfilAsync(int orderId, CancellationToken ct);

    Task<CancelOrderResult> CancelAsync(int orderId, CancellationToken ct);

    Task<RefundOrderResult> RefundAsync(
        string buyerId,
        int orderId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken ct);
}

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveAsync(string buyerId, CardPaymentDetails card, CancellationToken ct);
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken ct);
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}

public sealed record ReconciliationRow(
    string Match,
    string? EshopOrderId,
    string? EshopPaymentStatus,
    string? PayPalTransactionId,
    string? PayPalReferenceId,
    string? InvoiceId,
    string? Amount,
    string? Fee,
    string? Status,
    string? InitiationDate);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EshopPaymentCount,
    int Matched,
    int PayPalOnly,
    int EshopOnly,
    IReadOnlyList<ReconciliationRow> Rows);

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
