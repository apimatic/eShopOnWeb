using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemInput> items,
        CancellationToken ct = default);

    Task<IReadOnlyList<(Order Order, Payment? Payment)>> GetShopperOrdersAsync(
        string buyerId, CancellationToken ct = default);

    Task<Payment> AuthorizePaymentAsync(int orderId, string buyerId, PaymentInput input,
        CancellationToken ct = default);

    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken ct = default);

    Task<Payment> CancelOrderAsync(int orderId, CancellationToken ct = default);

    Task<(OrderRefund Refund, string RefundId)> RefundOrderAsync(int orderId,
        decimal? amount, string idempotencyKey, CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationEntry>> GetReconciliationAsync(
        System.DateTimeOffset from, System.DateTimeOffset to, CancellationToken ct = default);

    Task<SavedCard> SaveCardAsync(string shopperId, SaveCardInput cardInput,
        CancellationToken ct = default);

    Task<IReadOnlyList<SavedCard>> GetSavedCardsAsync(string shopperId,
        CancellationToken ct = default);

    Task DeleteSavedCardAsync(string shopperId, string paymentMethodId,
        CancellationToken ct = default);
}

public class OrderItemInput
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public class PaymentInput
{
    public string? SavedCardId { get; init; }
    public string? CardNumber { get; init; }
    public string? CardExpiry { get; init; }
    public string? CardCvv { get; init; }
    public string? CardHolderName { get; init; }
    public string? BillingAddressLine1 { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingState { get; init; }
    public string? BillingPostalCode { get; init; }
    public string BillingCountryCode { get; init; } = "US";
}

public class SaveCardInput
{
    public string? CardHolderName { get; init; }
    public required string CardNumber { get; init; }
    public required string CardExpiry { get; init; }
    public required string CardCvv { get; init; }
    public string? BillingAddressLine1 { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingState { get; init; }
    public string? BillingPostalCode { get; init; }
    public required string BillingCountryCode { get; init; }
}

public class ReconciliationEntry
{
    public string? PayPalTransactionId { get; init; }
    public string? PayPalStatus { get; init; }
    public string? PayPalAmount { get; init; }
    public string? PayPalCurrency { get; init; }
    public string? PayPalDate { get; init; }
    public int? EShopOrderId { get; init; }
    public string? EShopOrderStatus { get; init; }
    public string MatchStatus { get; init; } = "unmatched";
}
