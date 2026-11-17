using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class PaymentMapper
{
    public static OrderResponse ToOrderResponse(Order order, string defaultCurrency)
    {
        return new OrderResponse
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            TotalCents = order.TotalCents(),
            Currency = string.IsNullOrWhiteSpace(order.PaymentCurrency) ? defaultCurrency : order.PaymentCurrency!,
            PaymentStatus = order.PaymentStatus.ToString(),
            FulfilmentStatus = order.FulfilmentStatus.ToString(),
            AuthorizationId = order.FinixAuthorizationId,
            AuthorizationState = order.FinixAuthorizationState,
            AuthorizationVoidState = order.FinixAuthorizationVoidState,
            AuthorizationExpiresAt = order.FinixAuthorizationExpiresAt,
            AuthorizationVoided = order.FinixAuthorizationVoided,
            AuthorizedAmountCents = order.AuthorizedAmountCents,
            CaptureId = order.FinixCaptureTransferId,
            CaptureState = order.FinixCaptureState,
            CapturedAmountCents = order.CapturedAmountCents,
            FeeCents = order.FinixFeeCents,
            NetProceedsCents = order.NetProceedsCents,
            RefundedAmountCents = order.RefundedAmountCents,
            RemainingRefundableCents = order.RemainingRefundableCents(),
            PaymentFailureMessage = order.PaymentFailureMessage,
            ShipToAddress = order.ShipToAddress is null
                ? null
                : new ShippingAddressRequest
                {
                    Street = order.ShipToAddress.Street,
                    City = order.ShipToAddress.City,
                    State = order.ShipToAddress.State,
                    Country = order.ShipToAddress.Country,
                    ZipCode = order.ShipToAddress.ZipCode
                },
            Items = order.OrderItems.Select(item => new OrderItemResponse
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                UnitPrice = item.UnitPrice,
                Units = item.Units,
                LineTotal = item.UnitPrice * item.Units
            }).ToList(),
            Refunds = order.Refunds
                .OrderBy(r => r.CreatedAt)
                .Select(r => ToRefundResponse(order, r, false))
                .ToList()
        };
    }

    public static RefundResponse ToRefundResponse(Order order, OrderRefund refund, bool idempotentReplay)
    {
        return new RefundResponse
        {
            OrderId = order.Id,
            RefundId = refund.FinixRefundId,
            AmountCents = refund.AmountCents,
            Amount = Money.FromCents(refund.AmountCents),
            State = refund.State,
            IdempotencyKey = refund.IdempotencyKey,
            CreatedAt = refund.CreatedAt,
            IdempotentReplay = idempotentReplay,
            TotalRefundedCents = order.RefundedAmountCents,
            RemainingRefundableCents = order.RemainingRefundableCents(),
            PaymentStatus = order.PaymentStatus.ToString()
        };
    }
}
