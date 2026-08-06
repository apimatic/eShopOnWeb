using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates paying for and refunding orders through PayPal. All operations are scoped to
/// the requesting shopper and are idempotent in effect: a repeated call never charges or
/// refunds twice.
/// </summary>
public interface IPaymentService
{
    /// <summary>Pays for the buyer's order using the given instrument and returns the updated order.</summary>
    Task<Order> PayOrderAsync(int orderId, string buyerId, PaymentInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>Refunds the buyer's order in full and returns the updated order.</summary>
    Task<Order> RefundOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
}
