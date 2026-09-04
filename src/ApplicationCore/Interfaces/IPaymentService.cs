using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record AuthorizeOrderResult(
    int OrderId,
    string OrderStatus,
    string PaymentStatus,
    decimal Amount,
    string Currency,
    string? AuthorizationId,
    string PaymentSourceDescription);

public sealed record CaptureOrderResult(
    int OrderId,
    string OrderStatus,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string? Currency);

public sealed record VoidOrderResult(int OrderId, string OrderStatus, string? AuthorizationId);

public sealed record RefundOrderResult(
    int OrderId,
    int RefundId,
    string PayPalRefundId,
    decimal Amount,
    decimal TotalRefunded,
    decimal CapturedAmount,
    string Currency,
    string OrderStatus);

/// <summary>Orchestrates the payment lifecycle of an order against the PayPal gateway.</summary>
public interface IPaymentService
{
    /// <summary>Authorizes the order total (a hold) with either card details or a saved card.</summary>
    Task<AuthorizeOrderResult> AuthorizeOrderAsync(int orderId, string buyerId, CardDetails? card, int? savedCardId);

    /// <summary>Captures the authorized payment; renews a stale authorization when needed.</summary>
    Task<CaptureOrderResult> CaptureOrderAsync(int orderId);

    /// <summary>Cancels an order before fulfilment, releasing any held funds.</summary>
    Task<VoidOrderResult> CancelOrderAsync(int orderId);

    /// <summary>Refunds a captured payment, in full or in part, idempotent under the given key.</summary>
    Task<RefundOrderResult> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey);
}

/// <summary>Manages a shopper's saved cards in the PayPal vault.</summary>
public interface ISavedCardService
{
    Task<SavedCardResult> SaveCardAsync(string buyerId, CardDetails card);
    Task<IReadOnlyList<SavedCardDto>> ListCardsAsync(string buyerId);
    Task DeleteCardAsync(string buyerId, int savedCardId);
}

public sealed record SavedCardResult(
    int SavedCardId,
    string Last4,
    string Brand,
    string Expiry,
    string CardholderName);

public sealed record SavedCardDto(
    int SavedCardId,
    string Last4,
    string Brand,
    string Expiry,
    string CardholderName,
    string Description,
    string CreatedAt);