using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    /// <summary>Charges the order total to the card. Only the order's buyer may pay it.</summary>
    Task<PayOrderResult> PayAsync(int orderId, string buyerId, EncryptedCardDetails card, CancellationToken cancellationToken);

    /// <summary>Refunds <paramref name="amount"/> (or everything still refundable when null) of a paid order.</summary>
    Task<RefundOrderResult> RefundAsync(int orderId, decimal? amount, string? reason, string requestedBy, CancellationToken cancellationToken);
}
