using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentException : Exception
{
    public string ErrorCode { get; }

    public OrderPaymentException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}

public interface IPaymentService
{
    /// <summary>Places an order from catalog items. The order starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<(int CatalogItemId, int Quantity)> items,
        Address shipToAddress);

    /// <summary>
    /// Authorizes (holds) the order total with PayPal, by raw card or by a saved card.
    /// Idempotent in effect: paying an already-authorized order returns the existing payment.
    /// </summary>
    Task<Payment> PayOrderAsync(Order order, PayPalCardPayment? card, int? savedPaymentMethodId);

    /// <summary>
    /// Captures the authorized payment. Renews a stale authorization when possible;
    /// throws an actionable conflict when renewal is impossible.
    /// </summary>
    Task<Payment> FulfilOrderAsync(Order order);

    /// <summary>Releases the held funds and cancels the order before fulfilment.</summary>
    Task<Payment?> CancelOrderAsync(Order order);

    /// <summary>Refunds a captured payment in full or in part, under a caller-supplied idempotency key.</summary>
    Task<PaymentRefund> RefundOrderAsync(Order order, decimal? amount, string idempotencyKey);

    /// <summary>Vaults a card for the shopper.</summary>
    Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId, PayPalCardPayment card);

    /// <summary>Lists the shopper's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> ListPaymentMethodsAsync(string buyerId);

    /// <summary>Removes one of the shopper's saved cards (also removes it from PayPal's vault).</summary>
    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId);

    /// <summary>Builds the reconciliation report for a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to);
}
