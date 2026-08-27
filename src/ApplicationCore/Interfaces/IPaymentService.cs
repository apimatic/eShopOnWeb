using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    /// <summary>
    /// Puts a hold on the order total with PayPal. Idempotent: paying an
    /// already-authorized order returns the existing payment.
    /// </summary>
    Task<Payment> AuthorizePaymentAsync(Order order, PayPalCardDetails? card,
        SavedPaymentMethod? savedPaymentMethod, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the held money. Renews a stale authorization when PayPal allows it.
    /// Idempotent: fulfilling an already-fulfilled order returns the existing payment.
    /// </summary>
    Task<Payment> CapturePaymentAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the shopper's held funds before fulfilment.
    /// Idempotent: cancelling an already-cancelled order is a no-op.
    /// </summary>
    Task<Payment?> CancelPaymentAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a captured payment, in full (amount = null) or in part.
    /// Repeating the same idempotency key never refunds twice.
    /// </summary>
    Task<PaymentRefund> RefundPaymentAsync(Order order, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default);

    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card,
        CancellationToken cancellationToken = default);

    Task DeleteSavedCardAsync(SavedPaymentMethod savedPaymentMethod,
        CancellationToken cancellationToken = default);
}
