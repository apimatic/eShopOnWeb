using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class PaymentMappings
{
    public static CardPaymentDetails ToCardDetails(this CardDto dto)
    {
        CardBillingAddress? billing = null;
        if (dto.BillingAddress is not null)
        {
            billing = new CardBillingAddress(
                CountryCode: dto.BillingAddress.CountryCode,
                AddressLine1: dto.BillingAddress.AddressLine1,
                AddressLine2: dto.BillingAddress.AddressLine2,
                AdminArea2: dto.BillingAddress.AdminArea2,
                AdminArea1: dto.BillingAddress.AdminArea1,
                PostalCode: dto.BillingAddress.PostalCode);
        }

        return new CardPaymentDetails(
            Number: dto.Number,
            Expiry: dto.Expiry,
            SecurityCode: dto.SecurityCode,
            Name: dto.Name,
            BillingAddress: billing);
    }

    public static CardPaymentDetails ToCardDetails(this SavePaymentMethodRequest request)
    {
        CardBillingAddress? billing = null;
        if (request.BillingAddress is not null)
        {
            billing = new CardBillingAddress(
                CountryCode: request.BillingAddress.CountryCode,
                AddressLine1: request.BillingAddress.AddressLine1,
                AddressLine2: request.BillingAddress.AddressLine2,
                AdminArea2: request.BillingAddress.AdminArea2,
                AdminArea1: request.BillingAddress.AdminArea1,
                PostalCode: request.BillingAddress.PostalCode);
        }

        return new CardPaymentDetails(
            Number: request.Number,
            Expiry: request.Expiry,
            SecurityCode: request.SecurityCode,
            Name: request.Name,
            BillingAddress: billing);
    }

    public static OrderSummaryDto ToSummary(this Order order)
    {
        return new OrderSummaryDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = "USD",
            PaymentStatus = order.PaymentStatus.ToString(),
            PayPalOrderId = order.PayPalOrderId,
            CaptureId = order.PaymentCaptureId,
            RefundId = order.PaymentRefundId,
            Items = order.OrderItems.Select(oi => new OrderItemDto
            {
                CatalogItemId = oi.ItemOrdered.CatalogItemId,
                ProductName = oi.ItemOrdered.ProductName,
                UnitPrice = oi.UnitPrice,
                Units = oi.Units
            }).ToList()
        };
    }

    public static PaymentMethodDto ToDto(this PaymentMethod paymentMethod)
    {
        return new PaymentMethodDto
        {
            PaymentMethodId = paymentMethod.Id,
            Alias = paymentMethod.Alias,
            Brand = paymentMethod.Brand,
            Last4 = paymentMethod.Last4,
            Expiry = paymentMethod.Expiry
        };
    }
}
