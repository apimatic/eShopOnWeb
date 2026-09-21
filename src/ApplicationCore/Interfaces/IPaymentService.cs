using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the payment flows: placing an order, authorizing it, fulfilling (capturing), cancelling
/// (voiding), refunding, and managing saved cards — coordinating persistence with the
/// <see cref="IPayPalPaymentGateway"/>. Enforces owner-scoping and the payment idempotency rules.
/// </summary>
public interface IPaymentService
{
    /// <summary>Places an order for the shopper from catalog items. Returns the created payment record.</summary>
    Task<OrderPayment> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines,
        ShippingAddressRequest? shipTo, CancellationToken ct);

    /// <summary>Authorizes (holds) the order total. Idempotent: a repeat is a no-op.</summary>
    Task<OrderPayment> PayOrderAsync(string buyerId, int orderId, PayInstruction instruction,
        CancellationToken ct);

    /// <summary>Operator action: captures the money, renewing a stale authorization if needed.</summary>
    Task<OrderPayment> FulfilOrderAsync(int orderId, CancellationToken ct);

    /// <summary>Operator action: voids the hold before fulfilment, releasing the funds.</summary>
    Task<OrderPayment> CancelOrderAsync(int orderId, CancellationToken ct);

    /// <summary>Refunds a captured payment, in full or in part, under a caller-supplied idempotency key.</summary>
    Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken ct);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPayment>> GetMyOrderPaymentsAsync(string buyerId, CancellationToken ct);

    /// <summary>Saves a card for the shopper (vaulting it at PayPal).</summary>
    Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId, CardDetails card, CancellationToken ct);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> GetPaymentMethodsAsync(string buyerId, CancellationToken ct);

    /// <summary>Removes a saved card so it no longer appears and can no longer be used to pay.</summary>
    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken ct);

    /// <summary>Operator action: reconciles PayPal transactions against eShop orders for a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
