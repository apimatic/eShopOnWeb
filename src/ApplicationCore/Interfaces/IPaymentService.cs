using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    Task<OrderAndPayment> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items,
        Address shipToAddress, CancellationToken ct);

    Task<Payment> PayOrderAsync(string buyerId, int orderId, CardPaymentDetails? card,
        int? savedPaymentMethodId, CancellationToken ct);

    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken ct);

    Task<Payment> CancelOrderAsync(int orderId, CancellationToken ct);

    Task<PaymentRefund> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey,
        CancellationToken ct);

    Task<IReadOnlyList<OrderAndPayment>> ListOrdersAsync(string buyerId, CancellationToken ct);

    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentDetails card, CancellationToken ct);

    Task<IReadOnlyList<SavedPaymentMethod>> ListSavedCardsAsync(string buyerId, CancellationToken ct);

    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class OrderAndPayment
{
    public Order Order { get; set; } = null!;
    public Payment? Payment { get; set; }
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntry> Transactions { get; set; } = new List<ReconciliationEntry>();
    public List<Payment> PaymentsMissingInPayPal { get; set; } = new List<Payment>();
}

public class ReconciliationEntry
{
    public GatewayTransaction Transaction { get; set; } = new GatewayTransaction();

    /// <summary>Order hint from the invoice/custom id naming convention (informational).</summary>
    public int? OrderId { get; set; }

    /// <summary>Payment hint from the invoice/custom id naming convention (informational).</summary>
    public int? PaymentId { get; set; }

    /// <summary>True only when the transaction was confirmed against a locally recorded
    /// PayPal id or exact invoice id — never on naming-convention hints alone.</summary>
    public bool Matched { get; set; }
}
