using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class PlaceOrderItem
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public sealed class ShopOrderResult
{
    public required int OrderId { get; init; }
    public required string Status { get; init; }
    public required decimal Total { get; init; }
    public required string Currency { get; init; }
    public required DateTimeOffset OrderDate { get; init; }
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? AuthorizationExpiration { get; init; }
    public string? CaptureId { get; init; }
    public string? CaptureStatus { get; init; }
    public decimal? CapturedAmount { get; init; }
    public decimal? PaypalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public decimal RemainingRefundable { get; init; }
    public IReadOnlyList<ShopOrderItemResult> Items { get; init; } = Array.Empty<ShopOrderItemResult>();
    public IReadOnlyList<ShopRefundResult> Refunds { get; init; } = Array.Empty<ShopRefundResult>();
}

public sealed class ShopOrderItemResult
{
    public int CatalogItemId { get; init; }
    public required string ProductName { get; init; }
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
}

public sealed class ShopRefundResult
{
    public required int RefundId { get; init; }
    public required string PayPalRefundId { get; init; }
    public required string Status { get; init; }
    public decimal Amount { get; init; }
    public required string Currency { get; init; }
}

public interface IShopOrderService
{
    Task<ShopOrderResult> PlaceAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items, Address? shipTo, CancellationToken ct);
    Task<ShopOrderResult> PayAsync(string buyerId, int orderId, CardPaymentSource? card, int? paymentMethodId, CancellationToken ct);
    Task<ShopOrderResult> FulfilAsync(int orderId, CancellationToken ct);
    Task<ShopOrderResult> CancelAsync(int orderId, CancellationToken ct);
    Task<ShopRefundResult> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);
    Task<IReadOnlyList<ShopOrderResult>> ListMineAsync(string buyerId, CancellationToken ct);
}

public sealed class SavedCardResult
{
    public required int PaymentMethodId { get; init; }
    public string? LastDigits { get; init; }
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

public interface ISavedPaymentMethodService
{
    Task<SavedCardResult> SaveAsync(string buyerId, CardPaymentSource card, CancellationToken ct);
    Task<IReadOnlyList<SavedCardResult>> ListAsync(string buyerId, CancellationToken ct);
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}

public sealed class ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required IReadOnlyList<ReconciliationMatch> Matched { get; init; }
    public required IReadOnlyList<PayPalTransactionRecord> PayPalOnly { get; init; }
    public required IReadOnlyList<ShopOrderResult> EshopOnly { get; init; }
}

public sealed class ReconciliationMatch
{
    public required PayPalTransactionRecord PayPal { get; init; }
    public required ShopOrderResult Order { get; init; }
}

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
