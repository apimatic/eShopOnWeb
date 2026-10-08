using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Persists payment attempts and refunds. The Add methods are the duplicate-prevention claims: they insert a
/// row whose key a concurrent duplicate request would also compute, and the store refuses the second insert.
/// </summary>
public interface IPaymentStore
{
    /// <exception cref="Exceptions.PaymentConflictException">Another request already claimed this attempt.</exception>
    Task AddPaymentClaimAsync(OrderPayment payment, CancellationToken cancellationToken);

    /// <exception cref="Exceptions.PaymentConflictException">Another request already claimed this refund.</exception>
    Task AddRefundClaimAsync(OrderRefund refund, CancellationToken cancellationToken);

    /// <summary>Persists status changes made to attempts and refunds loaded or added in this unit of work.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
