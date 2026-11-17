using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using OrderEntity = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order;

namespace Microsoft.eShopWeb.PublicApi.Services;

public static class PaymentResponseMapper
{
    public static OrderResponse ToOrderResponse(
        OrderEntity order,
        OrderPayment? payment,
        IReadOnlyCollection<PaymentRefund>? refunds = null)
    {
        var total = order.Total();
        return new OrderResponse
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Status = payment?.Status.ToString() ?? PaymentStatus.AwaitingPayment.ToString(),
            Total = total,
            TotalCents = FinixPaymentService.ToCents(total),
            Currency = payment?.Currency ?? string.Empty,
            ShippingAddress = new AddressResponse
            {
                Street = order.ShipToAddress?.Street,
                City = order.ShipToAddress?.City,
                State = order.ShipToAddress?.State,
                Country = order.ShipToAddress?.Country,
                ZipCode = order.ShipToAddress?.ZipCode
            },
            Items = order.OrderItems.Select(item => new OrderItemResponse
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                PictureUri = item.ItemOrdered.PictureUri,
                UnitPrice = item.UnitPrice,
                Quantity = item.Units
            }).ToList(),
            Payment = ToPaymentResponse(payment, refunds)
        };
    }

    public static PaymentResponse ToPaymentResponse(
        OrderPayment? payment,
        IReadOnlyCollection<PaymentRefund>? refunds = null)
    {
        if (payment is null)
        {
            return new PaymentResponse
            {
                Status = PaymentStatus.AwaitingPayment.ToString()
            };
        }

        return new PaymentResponse
        {
            Status = payment.Status.ToString(),
            Currency = payment.Currency,
            AmountCents = payment.AmountCents,
            FinixAuthorizationId = payment.FinixAuthorizationId,
            AuthorizationIdempotencyId = payment.AuthorizationIdempotencyId,
            AuthorizationState = payment.AuthorizationState,
            AuthorizedAt = payment.AuthorizedAt,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            FinixTransferId = payment.FinixTransferId,
            TransferState = payment.TransferState,
            CapturedAt = payment.CapturedAt,
            CapturedAmountCents = payment.CapturedAmountCents,
            FinixFeeCents = payment.FinixFeeCents,
            NetProceedsCents = payment.NetProceedsCents,
            FulfilledAt = payment.FulfilledAt,
            CanceledAt = payment.CanceledAt,
            FailureCode = payment.FailureCode,
            FailureMessage = payment.FailureMessage,
            SavedPaymentMethodId = payment.SavedCardPublicId,
            Refunds = (refunds ?? [])
                .OrderBy(r => r.CreatedAt)
                .Select(ToRefundResponse)
                .ToList()
        };
    }

    public static RefundResponse ToRefundResponse(PaymentRefund refund) =>
        new()
        {
            RefundId = refund.PublicId,
            FinixTransferId = refund.FinixTransferId,
            State = refund.FinixState,
            AmountCents = refund.AmountCents,
            CreatedAt = refund.CreatedAt
        };

    public static SavedPaymentMethodResponse ToSavedPaymentMethodResponse(SavedCard card) =>
        new()
        {
            PaymentMethodId = card.PublicId,
            FinixPaymentInstrumentId = card.FinixPaymentInstrumentId,
            Brand = card.Brand,
            Last4 = card.Last4,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            CardholderName = card.CardholderName,
            CreatedAt = card.CreatedAt
        };
}
