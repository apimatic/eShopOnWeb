using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>Maps the Order + Payment aggregates to the read-only <see cref="OrderView"/>.</summary>
internal static class PaymentViewMapper
{
    public static OrderView ToView(Order order, Payment? payment, string currency)
    {
        PaymentSummary? summary = null;
        if (payment is not null)
        {
            summary = new PaymentSummary(
                payment.PayPalOrderId,
                payment.AuthorizationId,
                payment.AuthorizationStatus,
                payment.AuthorizationExpiresAt,
                payment.CaptureId,
                payment.CaptureStatus,
                payment.CapturedAmount,
                payment.PayPalFee,
                payment.NetAmount,
                payment.RefundedAmount,
                payment.PaymentMethodDescription,
                payment.Refunds
                    .Select(r => new RefundView(r.RefundId, r.Amount, r.Status))
                    .ToList());
        }

        return new OrderView(
            order.Id,
            order.OrderDate,
            order.PaymentStatus.ToString(),
            order.Total(),
            payment?.CurrencyCode ?? currency,
            summary);
    }
}
