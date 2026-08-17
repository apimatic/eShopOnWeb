using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>Projects <see cref="Payment"/> aggregates into the read models returned by the API.</summary>
internal static class PaymentViewMapper
{
    public static PaymentView ToView(Payment payment)
    {
        var refunds = payment.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RefundView(r.Id, r.PayPalRefundId, r.Amount, r.Currency, r.Status, r.IdempotencyKey, r.CreatedAt))
            .ToList();

        return new PaymentView(
            payment.OrderId,
            payment.BuyerId,
            payment.Currency,
            payment.Amount,
            payment.Status.ToString(),
            payment.PayPalOrderId,
            payment.AuthorizationId,
            payment.AuthorizationStatus,
            payment.AuthorizationExpiresAt,
            payment.CaptureId,
            payment.CaptureStatus,
            payment.CapturedAmount,
            payment.PayPalFee,
            payment.NetAmount,
            payment.CardBrand,
            payment.CardLast4,
            payment.RefundedAmount(),
            payment.RemainingRefundable(),
            payment.FailureReason,
            refunds);
    }
}
