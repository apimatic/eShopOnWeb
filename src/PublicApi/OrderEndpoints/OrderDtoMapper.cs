using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class OrderDtoMapper
{
    public static OrderDto ToDto(Order order, string? fallbackCurrency = null)
    {
        var currency = order.Currency ?? fallbackCurrency;
        return new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = currency,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = new PaymentStateDto
            {
                Status = order.Status.ToString(),
                PayPalCheckoutOrderId = order.PayPalCheckoutOrderId,
                PayPalAuthorizationId = order.PayPalAuthorizationId,
                PayPalAuthorizationStatus = order.PayPalAuthorizationStatus,
                PayPalCaptureId = order.PayPalCaptureId,
                PayPalCaptureStatus = order.PayPalCaptureStatus,
                AuthorizedAmount = order.AuthorizedAmount,
                CapturedAmount = order.CapturedAmount,
                PaypalFee = order.PaypalFee,
                NetProceeds = order.NetProceeds,
                RefundedAmount = order.RefundedAmount,
                Currency = currency,
                Refunds = order.Refunds.Select(ToRefundDto).ToList()
            }
        };
    }

    public static OrderRefundDto ToRefundDto(OrderRefund refund) => new()
    {
        RefundId = refund.Id,
        PayPalRefundId = refund.PayPalRefundId,
        Status = refund.PayPalRefundStatus,
        Amount = refund.Amount,
        Currency = refund.Currency
    };

    public static CardPaymentDetails ToCard(CardDetailsRequest card)
    {
        CardBillingAddress? billing = null;
        if (card.BillingAddress != null)
        {
            billing = new CardBillingAddress(
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.AdminArea2,
                card.BillingAddress.AdminArea1,
                card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode ?? "US");
        }

        return new CardPaymentDetails(
            card.Number,
            card.Expiry,
            card.SecurityCode,
            card.Name,
            billing);
    }
}
