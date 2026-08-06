using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Pays for and refunds orders through the payment provider. All operations are scoped to the
/// owning shopper (<c>buyerId</c>) and are idempotent in effect — a repeated pay or refund for an
/// order never charges or refunds twice. A missing or non-owned order yields
/// <see cref="ResultStatus.NotFound"/>; an operation invalid for the order's current payment state
/// yields <see cref="ResultStatus.Conflict"/>. Provider failures surface as
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.PaymentGatewayException"/>.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Pay for an order with one-off card details.</summary>
    Task<Result<Order>> PayWithCardAsync(string buyerId, int orderId, CardDetails card,
        CancellationToken cancellationToken = default);

    /// <summary>Pay for an order with one of the shopper's saved cards.</summary>
    Task<Result<Order>> PayWithSavedCardAsync(string buyerId, int orderId, int savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>Refund an order's payment in full.</summary>
    Task<Result<Order>> RefundAsync(string buyerId, int orderId,
        CancellationToken cancellationToken = default);
}
