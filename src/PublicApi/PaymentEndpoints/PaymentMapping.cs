using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public static class PaymentMapping
{
    public static OrderResponse ToOrderResponse(Order order)
    {
        var payment = order.Payment;
        return new OrderResponse
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = payment?.Currency,
            Items = order.OrderItems.Select(i => new OrderItemResponse
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = payment is null ? null : ToPaymentResponse(payment)
        };
    }

    public static PaymentResponse ToPaymentResponse(Payment payment) => new()
    {
        Status = payment.Status.ToString(),
        AmountCents = payment.AmountCents,
        Amount = payment.AmountCents / 100m,
        Currency = payment.Currency,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureTransferId = payment.CaptureTransferId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmountCents = payment.CapturedAmountCents,
        CapturedAmount = payment.CapturedAmountCents is null ? null : payment.CapturedAmountCents / 100m,
        FinixFeeCents = payment.FinixFeeCents,
        FinixFee = payment.FinixFeeCents is null ? null : payment.FinixFeeCents / 100m,
        NetProceedsCents = payment.NetProceedsCents,
        NetProceeds = payment.NetProceedsCents is null ? null : payment.NetProceedsCents / 100m,
        RefundedAmountCents = payment.RefundedAmountCents,
        RefundedAmount = payment.RefundedAmountCents / 100m,
        CapturedAt = payment.CapturedAt,
        FailureCode = payment.FailureCode,
        FailureMessage = payment.FailureMessage,
        Refunds = payment.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(ToRefundResponse)
            .ToList()
    };

    public static RefundResponse ToRefundResponse(Refund refund) => new()
    {
        RefundId = refund.Id,
        FinixTransferId = refund.FinixTransferId,
        AmountCents = refund.AmountCents,
        Amount = refund.AmountCents / 100m,
        Status = refund.Status,
        IdempotencyKey = refund.IdempotencyKey,
        CreatedAt = refund.CreatedAt
    };

    public static SavedPaymentMethodResponse ToSavedPaymentMethodResponse(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        LastFour = method.LastFour,
        ExpirationMonth = method.ExpirationMonth,
        ExpirationYear = method.ExpirationYear,
        Name = method.Name,
        CreatedAt = method.CreatedAt
    };
}
