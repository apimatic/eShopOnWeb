using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    /// <summary>
    /// Pays for the given order via PayPal, using either one-off <paramref name="card"/> details or a
    /// previously saved card (<paramref name="savedPaymentMethodId"/>). Exactly one must be supplied.
    /// The order must belong to <paramref name="buyerId"/>. Idempotent in effect: paying an
    /// already-paid order returns the existing payment without charging again.
    /// </summary>
    Task<PaymentResult> PayOrderAsync(
        string buyerId, int orderId, PaymentCard? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fully refunds the order's payment via PayPal. The order must belong to <paramref name="buyerId"/>
    /// and be paid. Idempotent in effect: refunding an already-refunded order does not refund again.
    /// </summary>
    Task<PaymentResult> RefundOrderAsync(
        string buyerId, int orderId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a pay/refund operation, describing the order's resulting payment state.</summary>
public class PaymentResult
{
    public PaymentResult(int orderId, PaymentStatus paymentStatus, decimal amount, string currencyCode,
        string? payPalOrderId, string? captureId, string? refundId)
    {
        OrderId = orderId;
        PaymentStatus = paymentStatus;
        Amount = amount;
        CurrencyCode = currencyCode;
        PayPalOrderId = payPalOrderId;
        CaptureId = captureId;
        RefundId = refundId;
    }

    public int OrderId { get; }
    public PaymentStatus PaymentStatus { get; }
    public decimal Amount { get; }
    public string CurrencyCode { get; }
    public string? PayPalOrderId { get; }
    public string? CaptureId { get; }
    public string? RefundId { get; }
}
